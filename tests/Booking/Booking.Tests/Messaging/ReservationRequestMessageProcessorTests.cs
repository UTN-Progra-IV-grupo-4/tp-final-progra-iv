using Booking.Api.Messaging;
using Booking.Business.Reservations;
using Microsoft.Extensions.Logging;

namespace Booking.Tests.Messaging;

public sealed class ReservationRequestMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_WhenDecisionIsComplete_CompletesAndLogsSafeIdentifiers()
    {
        var requestId = Guid.NewGuid();
        var correlationId = "correlation-123";
        var handler = StubHandler.Returning(
            ReservationRequestHandlingResult.Processed(
                new ReservationProcessingResult(
                    ReservationProcessingOutcome.Retained,
                    requestId)));
        var settlement = new RecordingSettlement();
        var logger = new RecordingLogger<ReservationRequestMessageProcessor>();
        var processor = new ReservationRequestMessageProcessor(handler, logger);
        var delivery = CreateDelivery(requestId.ToString(), correlationId);

        await processor.ProcessAsync(delivery, settlement, CancellationToken.None);

        Assert.Equal(1, settlement.CompleteCount);
        Assert.Empty(settlement.DeadLetterCalls);
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains(requestId.ToString(), StringComparison.Ordinal) &&
            entry.Message.Contains(correlationId, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("secret-body", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.Null(entry.Exception));
    }

    [Fact]
    public async Task ProcessAsync_WhenDecisionIsDeadLetter_DeadLettersWithReason()
    {
        var handler = StubHandler.Returning(
            ReservationRequestHandlingResult.Invalid(
                ReservationRequestMappingErrorCodes.InvalidJson));
        var settlement = new RecordingSettlement();
        var processor = new ReservationRequestMessageProcessor(
            handler,
            new RecordingLogger<ReservationRequestMessageProcessor>());
        var delivery = CreateDelivery("message-id", "correlation-id");

        await processor.ProcessAsync(delivery, settlement, CancellationToken.None);

        Assert.Equal(0, settlement.CompleteCount);
        var deadLetter = Assert.Single(settlement.DeadLetterCalls);
        Assert.Equal(ReservationRequestMappingErrorCodes.InvalidJson, deadLetter.Reason);
        Assert.NotEmpty(deadLetter.Description);
    }

    [Fact]
    public async Task ProcessAsync_WhenHandlerThrows_PropagatesWithoutSettlement()
    {
        var expectedException = new InvalidOperationException("secret-body");
        var handler = StubHandler.Throwing(expectedException);
        var settlement = new RecordingSettlement();
        var logger = new RecordingLogger<ReservationRequestMessageProcessor>();
        var processor = new ReservationRequestMessageProcessor(handler, logger);

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(
                CreateDelivery("message-id", "correlation-id"),
                settlement,
                CancellationToken.None));

        Assert.Same(expectedException, actualException);
        Assert.Equal(0, settlement.CompleteCount);
        Assert.Empty(settlement.DeadLetterCalls);
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("secret-body", StringComparison.Ordinal));
        Assert.All(logger.Entries, entry => Assert.Null(entry.Exception));
    }

    [Fact]
    public async Task ProcessAsync_WhenCompleteFails_PropagatesWithoutDeadLetter()
    {
        var expectedException = new InvalidOperationException("complete-failed");
        var handler = StubHandler.Returning(
            ReservationRequestHandlingResult.Processed(
                new ReservationProcessingResult(
                    ReservationProcessingOutcome.Duplicate,
                    Guid.NewGuid())));
        var settlement = new RecordingSettlement
        {
            CompleteException = expectedException
        };
        var processor = new ReservationRequestMessageProcessor(
            handler,
            new RecordingLogger<ReservationRequestMessageProcessor>());

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(
                CreateDelivery("message-id", "correlation-id"),
                settlement,
                CancellationToken.None));

        Assert.Same(expectedException, actualException);
        Assert.Equal(1, settlement.CompleteCount);
        Assert.Empty(settlement.DeadLetterCalls);
    }

    [Fact]
    public async Task ProcessAsync_WhenDeadLetterFails_PropagatesWithoutCompleting()
    {
        var expectedException = new InvalidOperationException("dead-letter-failed");
        var handler = StubHandler.Returning(
            ReservationRequestHandlingResult.Invalid(
                ReservationRequestMappingErrorCodes.MessageIdMismatch));
        var settlement = new RecordingSettlement
        {
            DeadLetterException = expectedException
        };
        var processor = new ReservationRequestMessageProcessor(
            handler,
            new RecordingLogger<ReservationRequestMessageProcessor>());

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(
                CreateDelivery("message-id", "correlation-id"),
                settlement,
                CancellationToken.None));

        Assert.Same(expectedException, actualException);
        Assert.Equal(0, settlement.CompleteCount);
        Assert.Single(settlement.DeadLetterCalls);
    }

    private static ReservationRequestDelivery CreateDelivery(
        string messageId,
        string correlationId) =>
        new(
            BinaryData.FromString("secret-body"),
            messageId,
            correlationId,
            null,
            2);

    private sealed class StubHandler : IReservationRequestMessageHandler
    {
        private readonly ReservationRequestHandlingResult? result;
        private readonly Exception? exception;

        private StubHandler(
            ReservationRequestHandlingResult? result,
            Exception? exception)
        {
            this.result = result;
            this.exception = exception;
        }

        public static StubHandler Returning(ReservationRequestHandlingResult result) =>
            new(result, null);

        public static StubHandler Throwing(Exception exception) =>
            new(null, exception);

        public Task<ReservationRequestHandlingResult> HandleAsync(
            ReservationRequestDelivery delivery,
            CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                return Task.FromException<ReservationRequestHandlingResult>(exception);
            }

            return Task.FromResult(result!);
        }
    }

    private sealed class RecordingSettlement : IReservationRequestMessageSettlement
    {
        public int CompleteCount { get; private set; }

        public List<(string Reason, string Description)> DeadLetterCalls { get; } = [];

        public Exception? CompleteException { get; init; }

        public Exception? DeadLetterException { get; init; }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            CompleteCount++;
            return CompleteException is null
                ? Task.CompletedTask
                : Task.FromException(CompleteException);
        }

        public Task DeadLetterAsync(
            string reason,
            string description,
            CancellationToken cancellationToken)
        {
            DeadLetterCalls.Add((reason, description));
            return DeadLetterException is null
                ? Task.CompletedTask
                : Task.FromException(DeadLetterException);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
