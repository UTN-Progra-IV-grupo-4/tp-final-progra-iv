using Contracts.Payment;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Payment.Business;
using Payment.Data.Entities;
using Payment.Data.Repositories;
using Payment.Grpc.Services;
using Payment.Tests.Data;
using Payment.Tests.TestDoubles;

namespace Payment.Tests.Grpc;

// Llama al servicio gRPC directo (sin Kestrel) con un ServerCallContext de prueba, contra PostgreSQL real.
// Lo que se verifica es lo que ve Booking: el código gRPC y la respuesta.
[Collection(PostgreSqlCollection.Name)]
public sealed class PaymentGrpcServiceTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const long AmountCents = 150_000;
    private const string Currency = "ARS";

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
    public async Task Charge_WithValidRequest_ReturnsApproved()
    {
        var reservationId = Guid.NewGuid();

        var response = await CreateService().Charge(CreateRequest(reservationId), TestServerCallContext.Create());

        Assert.Equal(Contracts.Payment.ChargeStatus.Approved, response.Status);
        Assert.NotEqual(Guid.Empty, Guid.Parse(response.ChargeId));
        Assert.Equal(string.Empty, response.FailureReason);
        var charge = Assert.Single(await ReadChargesAsync());
        Assert.Equal(reservationId, charge.ReservationId);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("ars")]
    [InlineData("")]
    public async Task Charge_WithUnsupportedCurrency_ReturnsInvalidArgumentAndDoesNotCharge(string currency)
    {
        var request = CreateRequest(Guid.NewGuid(), currency: currency);

        await AssertStatusAsync(StatusCode.InvalidArgument, request);

        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await ReadChargesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public async Task Charge_WithNonPositiveAmount_ReturnsInvalidArgument(long amountCents)
    {
        await AssertStatusAsync(StatusCode.InvalidArgument, CreateRequest(Guid.NewGuid(), amountCents: amountCents));

        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Charge_WithAmountAboveCap_ReturnsInvalidArgument()
    {
        var cap = new ChargeOptions().MaxAmountCents;

        await AssertStatusAsync(StatusCode.InvalidArgument, CreateRequest(Guid.NewGuid(), amountCents: cap + 1));
        await AssertStatusAsync(StatusCode.InvalidArgument, CreateRequest(Guid.NewGuid(), amountCents: long.MaxValue));

        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Charge_WithAmountExactlyAtCap_ReturnsApproved()
    {
        var cap = new ChargeOptions().MaxAmountCents;

        var response = await CreateService().Charge(
            CreateRequest(Guid.NewGuid(), amountCents: cap), TestServerCallContext.Create());

        Assert.Equal(Contracts.Payment.ChargeStatus.Approved, response.Status);
    }

    [Theory]
    [MemberData(nameof(ReservationIdsOutsideFormatD))]
    public async Task Charge_WithReservationIdOutsideFormatD_ReturnsInvalidArgument(string reservationId)
    {
        var request = new ChargeRequest { ReservationId = reservationId, AmountCents = AmountCents, Currency = Currency };

        await AssertStatusAsync(StatusCode.InvalidArgument, request);

        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await ReadChargesAsync());
    }

    public static TheoryData<string> ReservationIdsOutsideFormatD()
    {
        var reservationId = Guid.NewGuid();
        return new TheoryData<string>
        {
            reservationId.ToString("N"),
            reservationId.ToString("B"),
            reservationId.ToString("P"),
            string.Empty,
            Guid.Empty.ToString("D"),
            "no-es-un-guid"
        };
    }

    [Fact]
    public async Task Charge_WithoutCorrelationIdHeader_LogsNullCorrelationId()
    {
        var logger = new CapturingLogger<PaymentGrpcService>();

        await CreateService(logger: logger).Charge(CreateRequest(Guid.NewGuid()), TestServerCallContext.Create());

        Assert.Equal<string?>([null], logger.CorrelationIds);
    }

    [Theory]
    [InlineData("abc-123_X.y:z")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")] // 64 caracteres, el máximo
    public async Task Charge_WithValidCorrelationId_LogsItUnchanged(string correlationId)
    {
        var logger = new CapturingLogger<PaymentGrpcService>();

        await CreateService(logger: logger).Charge(
            CreateRequest(Guid.NewGuid()), TestServerCallContext.Create(correlationId));

        Assert.Equal<string?>([correlationId], logger.CorrelationIds);
    }

    [Theory]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 caracteres
    [InlineData("con espacios")]
    [InlineData("<script>")]
    public async Task Charge_WithInvalidCorrelationId_LogsPlaceholderAndStillCharges(string correlationId)
    {
        var logger = new CapturingLogger<PaymentGrpcService>();

        var response = await CreateService(logger: logger).Charge(
            CreateRequest(Guid.NewGuid()), TestServerCallContext.Create(correlationId));

        Assert.Equal(Contracts.Payment.ChargeStatus.Approved, response.Status);
        Assert.Equal<string?>(["invalido"], logger.CorrelationIds);
    }

    [Fact]
    public async Task Charge_WhenDatabaseFailureIsTransientAndWrappedByEntityFramework_ReturnsUnavailable()
    {
        // EF envuelve la falla de Npgsql al guardar: InvalidOperationException > DbUpdateException > DbException.
        var wrapped = new InvalidOperationException(
            "falla al guardar",
            new DbUpdateException("falla al guardar", new TestTransientDbException()));

        await AssertStatusAsync(StatusCode.Unavailable, CreateRequest(Guid.NewGuid()), new FailingChargeRepository(wrapped));
    }

    [Fact]
    public async Task Charge_WhenDatabaseFailureIsTransientAndNotWrapped_ReturnsUnavailable()
    {
        await AssertStatusAsync(
            StatusCode.Unavailable,
            CreateRequest(Guid.NewGuid()),
            new FailingChargeRepository(new TestTransientDbException()));
    }

    [Fact]
    public async Task Charge_WhenGatewayIsUnavailable_ReturnsUnavailable()
    {
        gateway.FailNextCall(new HttpRequestException("pasarela caída"));

        await AssertStatusAsync(StatusCode.Unavailable, CreateRequest(Guid.NewGuid()));
    }

    [Fact]
    public async Task Charge_WhenChargeIsInProgress_ReturnsAborted()
    {
        await AssertStatusAsync(
            StatusCode.Aborted,
            CreateRequest(Guid.NewGuid()),
            new FailingChargeRepository(new ChargeInProgressException(Guid.NewGuid())));
    }

    [Fact]
    public async Task Charge_WhenInvalidOperationWrapsANonTransientDatabaseFailure_ReturnsInternal()
    {
        var nonTransient = new InvalidOperationException(
            "falla al guardar",
            new DbUpdateException("falla al guardar", new TestTransientDbException(isTransient: false)));

        await AssertStatusAsync(StatusCode.Internal, CreateRequest(Guid.NewGuid()), new FailingChargeRepository(nonTransient));
    }

    [Fact]
    public async Task Charge_WhenInvalidOperationHasNoDatabaseCause_ReturnsInternal()
    {
        await AssertStatusAsync(
            StatusCode.Internal,
            CreateRequest(Guid.NewGuid()),
            new FailingChargeRepository(new InvalidOperationException("inconsistencia")));
    }

    [Fact]
    public async Task Charge_WhenSameReservationHasDifferentAmount_ReturnsInvalidArgument()
    {
        var reservationId = Guid.NewGuid();
        await CreateService().Charge(CreateRequest(reservationId), TestServerCallContext.Create());

        await AssertStatusAsync(StatusCode.InvalidArgument, CreateRequest(reservationId, amountCents: AmountCents + 1));
    }

    [Fact]
    public async Task Charge_WhenEightIdenticalRequestsCompete_ApprovesOneAndAbortsTheRest()
    {
        const int concurrentRequests = 8;
        var reservationId = Guid.NewGuid();
        var gate = gateway.HoldNextCall();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Todos arrancan juntos: uno gana el índice único y queda retenido en la pasarela; los otros siete
        // encuentran un cobro Pending reciente y tienen que responder ABORTED sin cobrar.
        var calls = Enumerable.Range(0, concurrentRequests)
            .Select(_ => ChargeAfterAsync(start.Task, reservationId))
            .ToList();
        start.SetResult();

        await gateway.Entered.Task;
        await WaitUntilAsync(() => calls.Count(call => call.IsCompleted) == concurrentRequests - 1);
        gate.SetResult();
        var outcomes = await Task.WhenAll(calls);

        Assert.Equal(1, outcomes.Count(outcome => outcome == StatusCode.OK));
        Assert.Equal(concurrentRequests - 1, outcomes.Count(outcome => outcome == StatusCode.Aborted));
        Assert.Equal(1, gateway.Calls);
        Assert.Single(await ReadChargesAsync());
    }

    private async Task<StatusCode> ChargeAfterAsync(Task start, Guid reservationId)
    {
        await start;
        try
        {
            await CreateService().Charge(CreateRequest(reservationId), TestServerCallContext.Create());
            return StatusCode.OK;
        }
        catch (RpcException exception)
        {
            return exception.StatusCode;
        }
    }

    // Espera una condición con tope de tiempo, para que un test colgado falle en vez de bloquear la corrida.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private async Task AssertStatusAsync(StatusCode expected, ChargeRequest request, IChargeRepository? repository = null)
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            CreateService(repository).Charge(request, TestServerCallContext.Create()));

        Assert.Equal(expected, exception.StatusCode);
    }

    private static ChargeRequest CreateRequest(
        Guid reservationId,
        long amountCents = AmountCents,
        string currency = Currency) => new()
    {
        ReservationId = reservationId.ToString(),
        AmountCents = amountCents,
        Currency = currency
    };

    // Un servicio por pedido, cada uno con su DbContext, como el scope de una llamada gRPC.
    private PaymentGrpcService CreateService(
        IChargeRepository? repository = null,
        ILogger<PaymentGrpcService>? logger = null)
    {
        var chargeOptions = Options.Create(new ChargeOptions { SupportedCurrencies = [Currency] });
        if (repository is null)
        {
            var dbContext = fixture.CreateDbContext();
            dbContexts.Add(dbContext);
            repository = new ChargeRepository(dbContext);
        }

        var chargeService = new ChargeService(
            repository, gateway, chargeOptions, time, NullLogger<ChargeService>.Instance);

        return new PaymentGrpcService(chargeService, chargeOptions, logger ?? NullLogger<PaymentGrpcService>.Instance);
    }

    private async Task<List<Charge>> ReadChargesAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.Charges.AsNoTracking().ToListAsync();
    }
}
