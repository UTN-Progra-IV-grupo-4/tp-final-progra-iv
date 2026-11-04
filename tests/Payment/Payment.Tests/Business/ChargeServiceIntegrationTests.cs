using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Payment.Business;
using Payment.Data.Entities;
using Payment.Data.Repositories;
using Payment.Tests.Data;
using Payment.Tests.TestDoubles;

namespace Payment.Tests.Business;

[Collection(PostgreSqlCollection.Name)]
public sealed class ChargeServiceIntegrationTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const long AmountCents = 150_000;
    private const string Currency = "ARS";

    // Reloj falso: el vencimiento de un Pending se prueba adelantándolo, sin esperar 60 s reales.
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePaymentGateway gateway = new();
    private readonly List<IAsyncDisposable> dbContexts = [];

    public Task InitializeAsync() => fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var dbContext in dbContexts)
            await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task ChargeAsync_WhenRetriedWithSameReservation_ReturnsStoredChargeWithoutChargingAgain()
    {
        var reservationId = Guid.NewGuid();

        var first = await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);
        var retry = await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);

        Assert.Equal(ChargeOutcome.Approved, first.Outcome);
        Assert.False(first.IsReplay);
        Assert.True(retry.IsReplay);
        Assert.Equal(first.ChargeId, retry.ChargeId);
        Assert.Equal(1, gateway.Calls);
        Assert.Single(await ReadChargesAsync());
    }

    [Fact]
    public async Task ChargeAsync_WhenSameReservationHasDifferentAmount_ThrowsMismatchAndDoesNotCharge()
    {
        var reservationId = Guid.NewGuid();
        await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);

        // Un centavo de diferencia alcanza: el monto se compara exacto, no por aproximación.
        await Assert.ThrowsAsync<ChargeMismatchException>(() =>
            CreateService().ChargeAsync(reservationId, AmountCents + 1, Currency, CancellationToken.None));

        Assert.Equal(1, gateway.Calls);
        var charge = Assert.Single(await ReadChargesAsync());
        Assert.Equal(AmountCents, charge.AmountCents);
    }

    [Fact]
    public async Task ChargeAsync_WhenSameReservationHasDifferentCurrency_ThrowsMismatchAndDoesNotCharge()
    {
        var reservationId = Guid.NewGuid();
        await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);

        await Assert.ThrowsAsync<ChargeMismatchException>(() =>
            CreateService().ChargeAsync(reservationId, AmountCents, "USD", CancellationToken.None));

        Assert.Equal(1, gateway.Calls);
        var charge = Assert.Single(await ReadChargesAsync());
        Assert.Equal(Currency, charge.Currency);
    }

    [Fact]
    public async Task ChargeAsync_WhenTwoRequestsCompete_ChargesOnceAndTheOtherSeesItInProgress()
    {
        var reservationId = Guid.NewGuid();
        var gate = gateway.HoldNextCall();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Los dos arrancan juntos: uno gana el índice único y queda retenido en la pasarela; el otro,
        // haya perdido el INSERT o encontrado la fila, tiene que ver un cobro en curso y no cobrar.
        var first = ChargeAfterAsync(start.Task, reservationId);
        var second = ChargeAfterAsync(start.Task, reservationId);
        start.SetResult();

        await gateway.Entered.Task;
        var loser = await Task.WhenAny(first, second);
        await Assert.ThrowsAsync<ChargeInProgressException>(() => loser);

        gate.SetResult();
        var winner = await (loser == first ? second : first);

        Assert.Equal(ChargeOutcome.Approved, winner.Outcome);
        Assert.Equal(1, gateway.Calls);
        Assert.Single(await ReadChargesAsync());
    }

    [Fact]
    public async Task ChargeAsync_WhenGatewayFailed_KeepsFailedAndRetryReclaimsSameCharge()
    {
        var reservationId = Guid.NewGuid();
        gateway.FailNextCall(new HttpRequestException("pasarela caída"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None));
        var failed = Assert.Single(await ReadChargesAsync());
        Assert.Equal(ChargeStatus.Failed, failed.Status);

        var retry = await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);

        Assert.Equal(ChargeOutcome.Approved, retry.Outcome);
        Assert.Equal(failed.Id, retry.ChargeId);
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task ChargeAsync_WhenPendingIsRecent_ThrowsInProgress_AndAfterTimeoutReclaimsIt()
    {
        // Un Pending que nadie va a terminar: el proceso que lo reclamó se cayó.
        var reservationId = Guid.NewGuid();
        var orphanId = await InsertPendingAsync(reservationId);

        await Assert.ThrowsAsync<ChargeInProgressException>(() =>
            CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None));
        Assert.Equal(0, gateway.Calls);

        time.Advance(new ChargeOptions().PendingTimeout + TimeSpan.FromSeconds(1));
        var reclaimed = await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);

        Assert.Equal(ChargeOutcome.Approved, reclaimed.Outcome);
        Assert.Equal(orphanId, reclaimed.ChargeId);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task ChargeAsync_WhenSlowChargeFinishesAfterBeingReclaimed_DiscardsItsResult()
    {
        var reservationId = Guid.NewGuid();
        var gate = gateway.HoldNextCall();

        // El primer pedido queda en la pasarela más que PendingTimeout, sin caerse: es el "zombi".
        var zombie = CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);
        await gateway.Entered.Task;
        time.Advance(new ChargeOptions().PendingTimeout + TimeSpan.FromSeconds(1));

        var current = await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);
        gate.SetResult();

        // El token de concurrencia (claimed_at) impide que el zombi pise el resultado vigente.
        await Assert.ThrowsAsync<ChargeInProgressException>(() => zombie);
        Assert.Equal(ChargeOutcome.Approved, current.Outcome);
        var charge = Assert.Single(await ReadChargesAsync());
        Assert.Equal(ChargeStatus.Approved, charge.Status);
        Assert.Equal(time.GetUtcNow().UtcDateTime, charge.ClaimedAt);
        Assert.Equal(2, gateway.Calls);
    }

    // Un servicio por pedido, cada uno con su DbContext, como el scope de una llamada gRPC.
    private ChargeService CreateService()
    {
        var dbContext = fixture.CreateDbContext();
        dbContexts.Add(dbContext);

        return new ChargeService(
            new ChargeRepository(dbContext),
            gateway,
            Options.Create(new ChargeOptions()),
            time,
            NullLogger<ChargeService>.Instance);
    }

    private async Task<ChargeResult> ChargeAfterAsync(Task start, Guid reservationId)
    {
        await start;
        return await CreateService().ChargeAsync(reservationId, AmountCents, Currency, CancellationToken.None);
    }

    private async Task<Guid> InsertPendingAsync(Guid reservationId)
    {
        await using var dbContext = fixture.CreateDbContext();
        var now = time.GetUtcNow().UtcDateTime;
        var charge = new Charge
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            AmountCents = AmountCents,
            Currency = Currency,
            Status = ChargeStatus.Pending,
            CreatedAt = now,
            ClaimedAt = now
        };
        dbContext.Charges.Add(charge);
        await dbContext.SaveChangesAsync();
        return charge.Id;
    }

    private async Task<List<Charge>> ReadChargesAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.Charges.AsNoTracking().ToListAsync();
    }
}
