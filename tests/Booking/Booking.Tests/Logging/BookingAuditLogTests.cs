using Booking.Api.Messaging;
using Booking.Business.Payments;
using Booking.Business.Reservations;
using Booking.Data.Models;
using Booking.Tests.TestDoubles;

namespace Booking.Tests.Logging;

// Start and end events of the chain in Booking (ticket #24): the fields that make a request traceable
// across services, and the final reservation state in the end event.
public sealed class BookingAuditLogTests : IDisposable
{
    private const string CorrelationId = "3f2b8d1e-0000-4000-8000-000000000024";
    private const int ReceivedEventId = 2001;
    private const int CompletedEventId = 2002;
    private const int DeadLetteredEventId = 2003;
    private const int LateApprovedEventId = 2005;

    private readonly RecordingLogs logs = new();

    public void Dispose() => logs.Dispose();

    [Theory]
    [InlineData(ReservationPaymentOutcome.Confirmed, ReservationStatus.Confirmed, "Confirmed")]
    [InlineData(ReservationPaymentOutcome.PaymentFailed, ReservationStatus.Rejected, "PaymentFailed")]
    [InlineData(ReservationPaymentOutcome.PaymentUnavailable, ReservationStatus.Rejected, "PaymentUnavailable")]
    [InlineData(ReservationPaymentOutcome.Expired, ReservationStatus.Expired, "Expired")]
    public async Task ProcessAsync_WhenPaymentSettles_LogsStartAndEndWithTheFinalReservationStatus(
        ReservationPaymentOutcome paymentOutcome,
        ReservationStatus finalStatus,
        string expectedResult)
    {
        var requestId = Guid.NewGuid();
        var result = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(ReservationProcessingOutcome.Retained, requestId),
            new ReservationPaymentResult(paymentOutcome, requestId, finalStatus, "charge-24"),
            correlationId: CorrelationId);

        await ProcessAsync(requestId, result);

        var received = Assert.Single(logs.Entries, entry => entry.EventId.Id == ReceivedEventId);
        Assert.Contains("Service: Booking", received.Message);
        Assert.Contains($"MessageId: {requestId}", received.Message);
        Assert.Contains($"CorrelationId: {CorrelationId}", received.Message);

        var completed = Assert.Single(logs.Entries, entry => entry.EventId.Id == CompletedEventId);
        Assert.Contains("Service: Booking", completed.Message);
        Assert.Contains($"RequestId: {requestId}", completed.Message);
        Assert.Contains($"CorrelationId: {CorrelationId}", completed.Message);
        Assert.Contains($"ReservationStatus: {finalStatus}", completed.Message);
        Assert.Contains($"Result: {expectedResult}", completed.Message);
        Assert.Contains("ChargeId: charge-24", completed.Message);
        Assert.Contains("SettlementAction: Complete", completed.Message);
        // The wording that made a rejected payment read as "Retained" is gone.
        Assert.DoesNotContain("Outcome: Retained", completed.Message);
    }

    [Fact]
    public async Task ProcessAsync_WhenSeatIsTaken_LogsRejectedWithSeatTakenResult()
    {
        var requestId = Guid.NewGuid();
        var result = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(ReservationProcessingOutcome.SeatTaken, requestId),
            correlationId: CorrelationId);

        await ProcessAsync(requestId, result);

        var completed = Assert.Single(logs.Entries, entry => entry.EventId.Id == CompletedEventId);
        Assert.Contains("ReservationStatus: Rejected", completed.Message);
        Assert.Contains("Result: SeatTaken", completed.Message);
    }

    [Fact]
    public async Task ProcessAsync_WhenLateApprovedPayment_LogsReviewAndDeadLetterWithTheErrorCode()
    {
        var requestId = Guid.NewGuid();
        var result = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(ReservationProcessingOutcome.Retained, requestId),
            new ReservationPaymentResult(
                ReservationPaymentOutcome.LateApproved, requestId, ReservationStatus.Expired, "charge-24"),
            correlationId: CorrelationId);

        await ProcessAsync(requestId, result);

        var late = Assert.Single(logs.Entries, entry => entry.EventId.Id == LateApprovedEventId);
        Assert.Contains($"CorrelationId: {CorrelationId}", late.Message);
        Assert.Contains("ChargeId: charge-24", late.Message);

        var deadLettered = Assert.Single(logs.Entries, entry => entry.EventId.Id == DeadLetteredEventId);
        Assert.Contains("Service: Booking", deadLettered.Message);
        Assert.Contains($"CorrelationId: {CorrelationId}", deadLettered.Message);
        Assert.Contains("ReservationStatus: Expired", deadLettered.Message);
        Assert.Contains("SettlementAction: DeadLetter", deadLettered.Message);
        Assert.Contains(
            $"ErrorCode: {ReservationRequestSettlementErrorCodes.PaymentApprovedAfterExpiry}",
            deadLettered.Message);
        Assert.DoesNotContain(logs.Entries, entry => entry.EventId.Id == CompletedEventId);
    }

    [Fact]
    public async Task ProcessAsync_NeverLogsTheMessageBody()
    {
        var requestId = Guid.NewGuid();
        var result = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(ReservationProcessingOutcome.Retained, requestId),
            new ReservationPaymentResult(
                ReservationPaymentOutcome.Confirmed, requestId, ReservationStatus.Confirmed, "charge-24"),
            correlationId: CorrelationId);

        await ProcessAsync(requestId, result);

        Assert.NotEmpty(logs.Entries);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain("secret-body", entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.Exception);
        });
    }

    private Task ProcessAsync(Guid requestId, ReservationRequestHandlingResult result)
    {
        var processor = new ReservationRequestMessageProcessor(
            new StubHandler(result), logs.CreateLogger<ReservationRequestMessageProcessor>());
        var delivery = new ReservationRequestDelivery(
            BinaryData.FromString("secret-body"), requestId.ToString(), CorrelationId, null, 1);

        return processor.ProcessAsync(delivery, new NoopSettlement(), CancellationToken.None);
    }

    private sealed class StubHandler(ReservationRequestHandlingResult result) : IReservationRequestMessageHandler
    {
        public Task<ReservationRequestHandlingResult> HandleAsync(
            ReservationRequestDelivery delivery,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class NoopSettlement : IReservationRequestMessageSettlement
    {
        public Task CompleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
