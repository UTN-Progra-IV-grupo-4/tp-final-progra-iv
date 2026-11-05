using Booking.Business.Payments;
using Booking.Data.Models;
using Booking.Data.Repositories;
using Booking.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationPaymentTransitionIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmedReservation_CannotBeRejectedByAnotherProcessor()
    {
        await fixture.ResetAsync();
        var reservationId = await SeedAsync(UtcNow.AddMinutes(10));

        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var confirmed = await new ReservationRepository(firstContext).TrySetPaymentOutcomeAsync(
            reservationId, ReservationStatus.Confirmed, null, UtcNow);
        var rejected = await new ReservationRepository(secondContext).TrySetPaymentOutcomeAsync(
            reservationId, ReservationStatus.Rejected, ReservationRejectionReason.PaymentUnavailable, UtcNow);

        Assert.True(confirmed);
        Assert.False(rejected);
        Assert.Equal(ReservationStatus.Confirmed, (await ReadAsync(reservationId)).Status);
    }

    [Fact]
    public async Task ExpiredReservation_CannotBeConfirmedAndReleasesTheSeat()
    {
        await fixture.ResetAsync();
        var reservationId = await SeedAsync(UtcNow.AddMinutes(10));

        await using var context = fixture.CreateDbContext();
        var repository = new ReservationRepository(context);
        var confirmed = await repository.TrySetPaymentOutcomeAsync(
            reservationId, ReservationStatus.Confirmed, null, UtcNow.AddMinutes(10));
        var expired = await repository.TryExpireAsync(reservationId, UtcNow.AddMinutes(10));

        Assert.False(confirmed);
        Assert.True(expired);
        var expiredReservation = await ReadAsync(reservationId);
        Assert.Equal(ReservationStatus.Expired, expiredReservation.Status);

        await using var replacementContext = fixture.CreateDbContext();
        var replacementResult = await new ReservationRepository(replacementContext).TryInsertAsync(
            new Reservation
            {
                Id = Guid.NewGuid(),
                EventId = expiredReservation.EventId,
                SeatId = expiredReservation.SeatId,
                Status = ReservationStatus.Retained,
                ExpiresAt = UtcNow.AddMinutes(20)
            });
        Assert.Equal(ReservationInsertOutcome.Created, replacementResult.Outcome);
    }

    [Fact]
    public async Task DeclinedPayment_PersistsRejectionReason()
    {
        await fixture.ResetAsync();
        var reservationId = await SeedAsync(UtcNow.AddMinutes(10));

        await using var context = fixture.CreateDbContext();
        var updated = await new ReservationRepository(context).TrySetPaymentOutcomeAsync(
            reservationId,
            ReservationStatus.Rejected,
            ReservationRejectionReason.PaymentFailed,
            UtcNow);

        Assert.True(updated);
        var stored = await ReadAsync(reservationId);
        Assert.Equal(ReservationStatus.Rejected, stored.Status);
        Assert.Equal(ReservationRejectionReason.PaymentFailed, stored.RejectionReason);
        Assert.Null(stored.ExpiresAt);
    }

    [Fact]
    public async Task PaymentConfirmationAndExpiryCompete_ExactlyOneTerminalTransitionWins()
    {
        await fixture.ResetAsync();
        var expiresAt = UtcNow.AddMinutes(10);
        var reservationId = await SeedAsync(expiresAt);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> ConfirmAsync()
        {
            await using var context = fixture.CreateDbContext();
            await startGate.Task;
            return await new ReservationRepository(context).TrySetPaymentOutcomeAsync(
                reservationId, ReservationStatus.Confirmed, null, expiresAt.AddTicks(-1));
        }

        async Task<bool> ExpireAsync()
        {
            await using var context = fixture.CreateDbContext();
            await startGate.Task;
            return await new ReservationRepository(context).TryExpireAsync(reservationId, expiresAt);
        }

        var confirmationTask = ConfirmAsync();
        var expirationTask = ExpireAsync();
        startGate.SetResult();
        var confirmationWon = await confirmationTask;
        var expirationWon = await expirationTask;

        Assert.NotEqual(confirmationWon, expirationWon);
        var stored = await ReadAsync(reservationId);
        Assert.Equal(
            confirmationWon ? ReservationStatus.Confirmed : ReservationStatus.Expired,
            stored.Status);
    }

    [Fact]
    public async Task TwoPaymentConsumersCompete_OnlyOneTerminalTransitionWins()
    {
        await fixture.ResetAsync();
        var reservationId = await SeedAsync(UtcNow.AddMinutes(10));
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TransitionAsync(
            ReservationStatus status,
            ReservationRejectionReason? reason)
        {
            await using var context = fixture.CreateDbContext();
            await startGate.Task;
            return await new ReservationRepository(context).TrySetPaymentOutcomeAsync(
                reservationId, status, reason, UtcNow);
        }

        var confirmationTask = TransitionAsync(ReservationStatus.Confirmed, null);
        var rejectionTask = TransitionAsync(
            ReservationStatus.Rejected, ReservationRejectionReason.PaymentUnavailable);
        startGate.SetResult();
        var confirmed = await confirmationTask;
        var rejected = await rejectionTask;

        Assert.NotEqual(confirmed, rejected);
        var stored = await ReadAsync(reservationId);
        Assert.Equal(confirmed ? ReservationStatus.Confirmed : ReservationStatus.Rejected, stored.Status);
    }

    [Fact]
    public async Task TwoConcurrentPaymentProcessors_ConfirmOnceAndNeverReportLateApproval()
    {
        await fixture.ResetAsync();
        var reservationId = await SeedAsync(UtcNow.AddMinutes(10));
        var bothCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var paymentClient = new FakePaymentChargeClient();
        for (var consumer = 0; consumer < 2; consumer++)
        {
            paymentClient.EnqueueStep(async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    bothCalled.SetResult();
                }

                // Both consumers hold an approved charge before either one persists it.
                await bothCalled.Task.WaitAsync(cancellationToken);
                return new PaymentChargeResult(PaymentChargeOutcome.Approved, "charge-concurrent");
            });
        }

        async Task<ReservationPaymentResult> ConsumeAsync()
        {
            await using var context = fixture.CreateDbContext();
            var processor = new ReservationPaymentProcessor(
                new ReservationRepository(context),
                paymentClient,
                Options.Create(new PaymentProcessingOptions { Enabled = true, AmountCents = 150000 }),
                new FixedTimeProvider(UtcNow));
            return await processor.ProcessAsync(
                reservationId, "correlation-19", new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        }

        var results = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());

        Assert.Equal(2, paymentClient.Requests.Count);
        Assert.All(paymentClient.Requests, request => Assert.Equal(reservationId, request.ReservationId));
        Assert.Single(results, result => result.Outcome == ReservationPaymentOutcome.Confirmed);
        Assert.Single(results, result => result.Outcome == ReservationPaymentOutcome.AlreadyTerminal);
        Assert.Equal(ReservationStatus.Confirmed, (await ReadAsync(reservationId)).Status);
    }

    private async Task<Guid> SeedAsync(DateTimeOffset expiresAt)
    {
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            Status = ReservationStatus.Retained,
            ExpiresAt = expiresAt
        };
        await using var context = fixture.CreateDbContext();
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();
        return reservation.Id;
    }

    private async Task<Reservation> ReadAsync(Guid reservationId)
    {
        await using var context = fixture.CreateDbContext();
        return await context.Reservations.AsNoTracking().SingleAsync(reservation => reservation.Id == reservationId);
    }
}
