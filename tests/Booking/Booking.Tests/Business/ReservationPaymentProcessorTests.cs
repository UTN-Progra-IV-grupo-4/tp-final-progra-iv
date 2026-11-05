using Booking.Business.Payments;
using Booking.Data.Models;
using Booking.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Booking.Tests.Business;

public sealed class ReservationPaymentProcessorTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApprovedPayment_ConfirmsOnlyAnActiveReservation()
    {
        var (repository, client, clock, reservation) = Setup();
        client.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "charge-1"));

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, "correlation-1");

        Assert.Equal(ReservationPaymentOutcome.Confirmed, result.Outcome);
        Assert.Equal("charge-1", result.ChargeId);
        Assert.Equal(ReservationStatus.Confirmed, (await repository.GetByIdAsync(reservation.Id))!.Status);
        var request = Assert.Single(client.Requests);
        Assert.Equal(reservation.Id, request.ReservationId);
        Assert.Equal(150000, request.AmountCents);
        Assert.Equal("ARS", request.Currency);
        Assert.Equal("correlation-1", request.CorrelationId);
    }

    [Fact]
    public async Task DeclinedPayment_RejectsWithPaymentFailed()
    {
        var (repository, client, clock, reservation) = Setup();
        client.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Declined, "charge-2"));

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.PaymentFailed, result.Outcome);
        var stored = await repository.GetByIdAsync(reservation.Id);
        Assert.Equal(ReservationStatus.Rejected, stored!.Status);
        Assert.Equal(ReservationRejectionReason.PaymentFailed, stored.RejectionReason);
    }

    [Theory]
    [InlineData(PaymentChargeFailure.Unavailable)]
    [InlineData(PaymentChargeFailure.DeadlineExceeded)]
    public async Task TemporaryFailure_RetriesSameReservationIdAndPersistsRecoveredResult(
        PaymentChargeFailure failure)
    {
        var (repository, client, clock, reservation) = Setup();
        client.EnqueueFailure(failure);
        client.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "charge-3"));

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.Confirmed, result.Outcome);
        Assert.Equal(2, client.Requests.Count);
        Assert.All(client.Requests, request => Assert.Equal(reservation.Id, request.ReservationId));
    }

    [Fact]
    public async Task UnavailableAfterThreeAttempts_RejectsWithPaymentUnavailable()
    {
        var (repository, client, clock, reservation) = Setup();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            client.EnqueueFailure(PaymentChargeFailure.Unavailable);
        }

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.PaymentUnavailable, result.Outcome);
        Assert.True(result.PaymentOutcomeUnknown);
        Assert.Equal(3, client.Requests.Count);
        var stored = await repository.GetByIdAsync(reservation.Id);
        Assert.Equal(ReservationStatus.Rejected, stored!.Status);
        Assert.Equal(ReservationRejectionReason.PaymentUnavailable, stored.RejectionReason);
    }

    [Fact]
    public async Task ExpiredBeforeCharge_DoesNotCallPayment()
    {
        var (repository, client, clock, reservation) = Setup();
        clock.UtcNow = UtcNow.AddMinutes(10);

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.Expired, result.Outcome);
        Assert.Equal(ReservationStatus.Expired, (await repository.GetByIdAsync(reservation.Id))!.Status);
        Assert.Empty(client.Requests);
        Assert.False(result.PaymentOutcomeUnknown);
    }

    [Fact]
    public async Task UnavailableExhaustedAfterExpiry_PreservesUnknownOutcome()
    {
        var (repository, client, clock, reservation) = Setup();
        client.EnqueueFailure(PaymentChargeFailure.Unavailable);
        client.EnqueueStep((_, _) =>
        {
            clock.UtcNow = UtcNow.AddMinutes(10);
            return Task.FromException<PaymentChargeResult>(
                new PaymentChargeException(PaymentChargeFailure.DeadlineExceeded));
        });

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.Expired, result.Outcome);
        Assert.True(result.PaymentOutcomeUnknown);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task ReservationExpiresDuringRetryDelay_PreservesUnknownOutcomeWithoutAnotherCharge()
    {
        var (repository, client, _, reservation) = Setup();
        var clock = new FakeTimeProvider(UtcNow);
        client.EnqueueFailure(PaymentChargeFailure.Unavailable);
        var processor = CreateProcessor(repository, client, clock, TimeSpan.FromSeconds(1));

        var processing = processor.ProcessAsync(reservation.Id, null);
        clock.Advance(TimeSpan.FromMinutes(10));
        var result = await processing;

        Assert.Equal(ReservationPaymentOutcome.Expired, result.Outcome);
        Assert.True(result.PaymentOutcomeUnknown);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task RetryDelay_UsesTimeProviderBeforeSecondAttempt()
    {
        var (repository, client, _, reservation) = Setup();
        var clock = new FakeTimeProvider(UtcNow);
        client.EnqueueFailure(PaymentChargeFailure.Unavailable);
        client.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "charge-delayed"));
        var processor = CreateProcessor(repository, client, clock, TimeSpan.FromSeconds(2));

        var processing = processor.ProcessAsync(reservation.Id, null);
        Assert.Single(client.Requests);
        Assert.False(processing.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await processing;

        Assert.Equal(ReservationPaymentOutcome.Confirmed, result.Outcome);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task CancellationDuringRetryDelay_DoesNotCallPaymentAgain()
    {
        var (repository, client, _, reservation) = Setup();
        var clock = new FakeTimeProvider(UtcNow);
        client.EnqueueFailure(PaymentChargeFailure.Unavailable);
        using var cancellation = new CancellationTokenSource();
        var processor = CreateProcessor(repository, client, clock, TimeSpan.FromSeconds(2));

        var processing = processor.ProcessAsync(reservation.Id, null, cancellation.Token);
        Assert.Single(client.Requests);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Single(client.Requests);
        Assert.Equal(ReservationStatus.Retained, (await repository.GetByIdAsync(reservation.Id))!.Status);
    }

    [Fact]
    public async Task ApprovedAfterExpiry_LeavesReservationExpiredAndReportsLateCharge()
    {
        var (repository, client, clock, reservation) = Setup();
        client.EnqueueStep((_, _) =>
        {
            clock.UtcNow = UtcNow.AddMinutes(10);
            return Task.FromResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "late-charge"));
        });

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.LateApproved, result.Outcome);
        Assert.Equal("late-charge", result.ChargeId);
        Assert.Equal(ReservationStatus.Expired, (await repository.GetByIdAsync(reservation.Id))!.Status);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task PendingAfterThreeAttempts_LeavesReservationRetainedForF05Decision()
    {
        var (repository, client, clock, reservation) = Setup();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            client.EnqueueFailure(PaymentChargeFailure.Pending);
        }

        var exception = await Assert.ThrowsAsync<PaymentChargeException>(
            () => CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null));

        Assert.Equal(PaymentChargeFailure.Pending, exception.Failure);
        Assert.Equal(ReservationStatus.Retained, (await repository.GetByIdAsync(reservation.Id))!.Status);
        Assert.Equal(3, client.Requests.Count);
    }

    [Fact]
    public async Task TerminalReservation_DoesNotChargeAgain()
    {
        var (repository, client, clock, reservation) = Setup();
        reservation.Status = ReservationStatus.Confirmed;
        repository.Seed(reservation);

        var result = await CreateProcessor(repository, client, clock).ProcessAsync(reservation.Id, null);

        Assert.Equal(ReservationPaymentOutcome.AlreadyTerminal, result.Outcome);
        Assert.Empty(client.Requests);
    }

    private static (StubReservationRepository Repository, FakePaymentChargeClient Client,
        AdjustableTimeProvider Clock, Reservation Reservation) Setup()
    {
        var repository = new StubReservationRepository();
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            Status = ReservationStatus.Retained,
            ExpiresAt = UtcNow.AddMinutes(10)
        };
        repository.Seed(reservation);
        return (repository, new FakePaymentChargeClient(), new AdjustableTimeProvider(UtcNow), reservation);
    }

    private static ReservationPaymentProcessor CreateProcessor(
        StubReservationRepository repository,
        FakePaymentChargeClient client,
        TimeProvider clock,
        TimeSpan? retryDelay = null) =>
        new(repository, client, Options.Create(new PaymentProcessingOptions
        {
            AmountCents = 150000,
            Currency = "ARS",
            MaxAttempts = 3,
            RetryDelay = retryDelay ?? TimeSpan.Zero
        }), clock);

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
