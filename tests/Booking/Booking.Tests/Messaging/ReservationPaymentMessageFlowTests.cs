using Booking.Api.Messaging;
using Booking.Business.Payments;
using Booking.Business.Reservations;
using Booking.Data.Models;
using Booking.Data.Repositories;
using Booking.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Messaging;

public sealed class ReservationPaymentMessageFlowTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PaymentChargeOutcome.Approved, ReservationStatus.Confirmed, null)]
    [InlineData(PaymentChargeOutcome.Declined, ReservationStatus.Rejected, ReservationRejectionReason.PaymentFailed)]
    public async Task NewReservation_CompletesOnlyAfterPaymentStateIsPersisted(
        PaymentChargeOutcome chargeOutcome,
        ReservationStatus expectedStatus,
        ReservationRejectionReason? expectedReason)
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Payment.EnqueueResult(new PaymentChargeResult(chargeOutcome, "charge-1"));

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(settlement.DeadLetters);
        var stored = await harness.Repository.GetByIdAsync(harness.RequestId);
        Assert.Equal(expectedStatus, stored!.Status);
        Assert.Equal(expectedReason, stored.RejectionReason);
        var charge = Assert.Single(harness.Payment.Requests);
        Assert.Equal(harness.RequestId, charge.ReservationId);
        Assert.Equal("correlation-19", charge.CorrelationId);
        Assert.NotEqual("transport-correlation", charge.CorrelationId);
    }

    [Fact]
    public async Task CompatibleDuplicateWhileRetained_ContinuesPaymentWithSameReservationId()
    {
        await using var harness = new Harness();
        var retained = harness.CreateReservation();
        harness.Repository.Seed(retained);
        harness.Repository.EnqueueResult(ReservationInsertResult.RequestIdExists(retained));
        harness.Payment.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "charge-2"));

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Equal(harness.RequestId, Assert.Single(harness.Payment.Requests).ReservationId);
        Assert.Equal(ReservationStatus.Confirmed,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
    }

    [Fact]
    public async Task CompatibleDuplicateAlreadyTerminal_CompletesWithoutChargingAgain()
    {
        await using var harness = new Harness();
        var confirmed = harness.CreateReservation();
        confirmed.Status = ReservationStatus.Confirmed;
        harness.Repository.Seed(confirmed);
        harness.Repository.EnqueueResult(ReservationInsertResult.RequestIdExists(confirmed));

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(harness.Payment.Requests);
    }

    [Fact]
    public async Task PaymentDisabled_CompletesRetainedReservationWithoutCallingPayment()
    {
        await using var harness = new Harness(paymentEnabled: false);
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Payment.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "unused"));

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(harness.Payment.Requests);
        Assert.Equal(ReservationStatus.Retained,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
    }

    [Fact]
    public async Task PaymentUnavailableAfterRetries_CompletesRejectedReservation()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            harness.Payment.EnqueueFailure(PaymentChargeFailure.Unavailable);
        }

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Equal(3, harness.Payment.Requests.Count);
        Assert.All(harness.Payment.Requests, request => Assert.Equal(harness.RequestId, request.ReservationId));
        var stored = await harness.Repository.GetByIdAsync(harness.RequestId);
        Assert.Equal(ReservationStatus.Rejected, stored!.Status);
        Assert.Equal(ReservationRejectionReason.PaymentUnavailable, stored.RejectionReason);
        Assert.Contains(harness.Logs.Messages, message =>
            message.Contains("Possible late payment; payment result unknown", StringComparison.Ordinal) &&
            message.Contains("Service: Booking", StringComparison.Ordinal) &&
            message.Contains("ErrorCode: PaymentOutcomeUnknown", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal) &&
            message.Contains("correlation-19", StringComparison.Ordinal) &&
            !message.Contains("transport-correlation", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownPaymentOutcomeAfterExpiry_IsLoggedEvenWhenReservationExpires()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Payment.EnqueueFailure(PaymentChargeFailure.Unavailable);
        harness.Payment.EnqueueStep((_, _) =>
        {
            harness.Clock.UtcNow = UtcNow.AddMinutes(10);
            return Task.FromException<PaymentChargeResult>(
                new PaymentChargeException(PaymentChargeFailure.DeadlineExceeded));
        });

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Equal(ReservationStatus.Expired,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
        Assert.Contains(harness.Logs.Messages, message =>
            message.Contains("ErrorCode: PaymentOutcomeUnknown", StringComparison.Ordinal) &&
            message.Contains("ReservationStatus: Expired", StringComparison.Ordinal) &&
            message.Contains("SettlementAction: Complete", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PaymentChargeFailure.Pending, ReservationRequestSettlementErrorCodes.PaymentPending)]
    [InlineData(PaymentChargeFailure.InvalidRequest, ReservationRequestSettlementErrorCodes.PaymentInvalidRequest)]
    [InlineData(PaymentChargeFailure.UnexpectedResponse, ReservationRequestSettlementErrorCodes.PaymentUnexpectedResponse)]
    public async Task PaymentReviewCase_DeadLettersWithoutTerminalPaymentState(
        PaymentChargeFailure failure,
        string reason)
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        var attempts = failure == PaymentChargeFailure.Pending ? 3 : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            harness.Payment.EnqueueFailure(failure);
        }

        var settlement = await harness.ProcessAsync();

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(reason, Assert.Single(settlement.DeadLetters).Reason);
        Assert.Equal(ReservationStatus.Retained,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
    }

    [Theory]
    [InlineData(PaymentChargeFailure.Pending, ReservationRequestSettlementErrorCodes.PaymentPending)]
    [InlineData(PaymentChargeFailure.InvalidRequest, ReservationRequestSettlementErrorCodes.PaymentInvalidRequest)]
    public async Task ReviewCaseAfterAmbiguousAttempt_DeadLettersAndLogsPossibleLatePayment(
        PaymentChargeFailure finalFailure,
        string reason)
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Payment.EnqueueFailure(PaymentChargeFailure.Unavailable);
        harness.Payment.EnqueueFailure(finalFailure);
        if (finalFailure == PaymentChargeFailure.Pending)
        {
            harness.Payment.EnqueueFailure(PaymentChargeFailure.Pending);
        }

        var settlement = await harness.ProcessAsync();

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(reason, Assert.Single(settlement.DeadLetters).Reason);
        Assert.Equal(ReservationStatus.Retained,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
        Assert.Contains(harness.Logs.Messages, message =>
            message.Contains("ErrorCode: PaymentOutcomeUnknown", StringComparison.Ordinal) &&
            message.Contains("SettlementAction: DeadLetter", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingOnly_DeadLettersWithoutPossibleLatePaymentLog()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            harness.Payment.EnqueueFailure(PaymentChargeFailure.Pending);
        }

        await harness.ProcessAsync();

        Assert.DoesNotContain(harness.Logs.Messages, message =>
            message.Contains("PaymentOutcomeUnknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingAfterUnavailable_DeadLettersAndRecordsUnknownPriorOutcome()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Payment.EnqueueFailure(PaymentChargeFailure.Unavailable);
        harness.Payment.EnqueueFailure(PaymentChargeFailure.Pending);
        harness.Payment.EnqueueFailure(PaymentChargeFailure.Pending);

        var settlement = await harness.ProcessAsync();

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(ReservationRequestSettlementErrorCodes.PaymentPending,
            Assert.Single(settlement.DeadLetters).Reason);
        Assert.Equal(ReservationStatus.Retained,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
        Assert.Contains(harness.Logs.Messages, message =>
            message.Contains("ErrorCode: PaymentOutcomeUnknown", StringComparison.Ordinal) &&
            message.Contains("SettlementAction: DeadLetter", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal) &&
            message.Contains("correlation-19", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LateApproval_DeadLettersAndDoesNotConfirm()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Payment.EnqueueStep((_, _) =>
        {
            harness.Clock.UtcNow = UtcNow.AddMinutes(10);
            return Task.FromResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "late-charge"));
        });

        var settlement = await harness.ProcessAsync();

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(ReservationRequestSettlementErrorCodes.PaymentApprovedAfterExpiry,
            Assert.Single(settlement.DeadLetters).Reason);
        Assert.Equal(ReservationStatus.Expired,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
        Assert.Contains(harness.Logs.Messages, message =>
            message.Contains("Late approved payment requires review", StringComparison.Ordinal) &&
            message.Contains(ReservationRequestSettlementErrorCodes.PaymentApprovedAfterExpiry, StringComparison.Ordinal) &&
            message.Contains("Service: Booking", StringComparison.Ordinal) &&
            message.Contains("ChargeId: late-charge", StringComparison.Ordinal) &&
            message.Contains(harness.RequestId.ToString(), StringComparison.Ordinal) &&
            message.Contains("correlation-19", StringComparison.Ordinal) &&
            !message.Contains("transport-correlation", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Messages, message =>
            message.Contains("secret-user-payload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersistenceFailure_PropagatesWithoutSettlement()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Repository.PaymentTransitionException = new InvalidOperationException("database unavailable");
        harness.Payment.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "charge-3"));
        var settlement = new RecordingSettlement();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(settlement));

        Assert.Equal(0, settlement.Completed);
        Assert.Empty(settlement.DeadLetters);
    }

    [Fact]
    public async Task RedeliveryAfterPersistenceFailure_UsesSameReservationIdAndCompletesAfterSaving()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());
        harness.Repository.PaymentTransitionException = new InvalidOperationException("database unavailable");
        harness.Payment.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "same-charge"));
        var firstSettlement = new RecordingSettlement();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ProcessAsync(firstSettlement));

        Assert.Equal(0, firstSettlement.Completed);
        Assert.Empty(firstSettlement.DeadLetters);
        var retained = (await harness.Repository.GetByIdAsync(harness.RequestId))!;
        Assert.Equal(ReservationStatus.Retained, retained.Status);

        harness.Repository.PaymentTransitionException = null;
        harness.Repository.EnqueueResult(ReservationInsertResult.RequestIdExists(retained));
        harness.Payment.EnqueueResult(new PaymentChargeResult(PaymentChargeOutcome.Approved, "same-charge"));
        var secondSettlement = await harness.ProcessAsync();

        Assert.Equal(1, secondSettlement.Completed);
        Assert.Empty(secondSettlement.DeadLetters);
        Assert.Equal(ReservationStatus.Confirmed,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.Status);
        Assert.Equal(2, harness.Payment.Requests.Count);
        Assert.All(harness.Payment.Requests, request =>
            Assert.Equal(harness.RequestId, request.ReservationId));
    }

    [Fact]
    public async Task SeatTaken_CompletesWithoutCallingPayment()
    {
        await using var harness = new Harness();
        harness.Repository.EnqueueResult(ReservationInsertResult.SeatTaken());
        harness.Repository.EnqueueResult(ReservationInsertResult.Created());

        var settlement = await harness.ProcessAsync();

        Assert.Equal(1, settlement.Completed);
        Assert.Empty(harness.Payment.Requests);
        Assert.Equal(ReservationRejectionReason.SeatTaken,
            (await harness.Repository.GetByIdAsync(harness.RequestId))!.RejectionReason);
    }

    [Fact]
    public async Task ConflictingDuplicate_DeadLettersWithoutCallingPayment()
    {
        await using var harness = new Harness();
        var existing = harness.CreateReservation();
        existing.SeatId = Guid.NewGuid();
        harness.Repository.Seed(existing);
        harness.Repository.EnqueueResult(ReservationInsertResult.RequestIdExists(existing));

        var settlement = await harness.ProcessAsync();

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(ReservationProcessingErrorCodes.RequestPayloadConflict,
            Assert.Single(settlement.DeadLetters).Reason);
        Assert.Empty(harness.Payment.Requests);
    }

    [Fact]
    public async Task InvalidMessage_DeadLettersBeforeResolvingPayment()
    {
        await using var harness = new Harness();
        var invalidDelivery = new ReservationRequestDelivery(
            BinaryData.FromString("{invalid-json"),
            harness.RequestId.ToString(),
            "transport-correlation",
            null);

        var settlement = await harness.ProcessAsync(delivery: invalidDelivery);

        Assert.Equal(0, settlement.Completed);
        Assert.Equal(ReservationRequestMappingErrorCodes.InvalidJson,
            Assert.Single(settlement.DeadLetters).Reason);
        Assert.Empty(harness.Payment.Requests);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        private readonly ReservationRequestMessageProcessor processor;

        public Guid RequestId { get; } = Guid.NewGuid();
        public Guid EventId { get; } = Guid.NewGuid();
        public Guid SeatId { get; } = Guid.NewGuid();
        public StubReservationRepository Repository { get; } = new();
        public FakePaymentChargeClient Payment { get; } = new();
        public AdjustableTimeProvider Clock { get; } = new(UtcNow);
        public RecordingLogger<ReservationRequestMessageProcessor> Logs { get; } = new();

        public Harness(bool paymentEnabled = true)
        {
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IReservationRepository>(Repository);
            registrations.AddSingleton<IPaymentChargeClient>(Payment);
            registrations.AddSingleton<TimeProvider>(Clock);
            registrations.AddScoped<IReservationRequestProcessor, ReservationRequestProcessor>();
            registrations.AddScoped<ReservationPaymentProcessor>();
            registrations.AddSingleton(Options.Create(new ReservationProcessingOptions
            {
                RetentionDuration = TimeSpan.FromMinutes(10)
            }));
            registrations.AddSingleton(Options.Create(new PaymentProcessingOptions
            {
                AmountCents = 150000,
                Currency = "ARS",
                MaxAttempts = 3,
                RetryDelay = TimeSpan.Zero
            }));
            services = registrations.BuildServiceProvider();
            var handler = new ReservationRequestMessageHandler(
                services.GetRequiredService<IServiceScopeFactory>(),
                new ReservationRequestMapper(),
                Options.Create(new PaymentConsumerOptions { Enabled = paymentEnabled }));
            processor = new ReservationRequestMessageProcessor(
                handler,
                Logs);
        }

        public Reservation CreateReservation() => new()
        {
            Id = RequestId,
            EventId = EventId,
            SeatId = SeatId,
            Status = ReservationStatus.Retained,
            ExpiresAt = UtcNow.AddMinutes(10)
        };

        public async Task<RecordingSettlement> ProcessAsync(
            RecordingSettlement? settlement = null,
            ReservationRequestDelivery? delivery = null)
        {
            settlement ??= new RecordingSettlement();
            await processor.ProcessAsync(delivery ?? CreateDelivery(), settlement, CancellationToken.None);
            return settlement;
        }

        private ReservationRequestDelivery CreateDelivery() => new(
            BinaryData.FromString(
                $$"""
                  {
                    "messageId": "{{RequestId}}",
                    "correlationId": "correlation-19",
                    "eventId": "{{EventId}}",
                    "seatId": "{{SeatId}}",
                    "userId": "secret-user-payload",
                    "requestedAt": "2026-11-02T15:00:00Z"
                  }
                  """),
            RequestId.ToString(),
            "transport-correlation",
            null);

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    private sealed class RecordingSettlement : IReservationRequestMessageSettlement
    {
        public int Completed { get; private set; }
        public List<(string Reason, string Description)> DeadLetters { get; } = [];

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            Completed++;
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken)
        {
            DeadLetters.Add((reason, description));
            return Task.CompletedTask;
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
