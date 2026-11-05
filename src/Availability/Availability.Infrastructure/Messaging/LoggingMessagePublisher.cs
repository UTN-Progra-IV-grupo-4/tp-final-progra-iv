using Availability.Application.Abstractions;
using Availability.Application.Logging;
using Contracts;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Messaging;

// Stand-in used when no broker is configured: the request is only logged, never sent.
public class LoggingMessagePublisher : IMessagePublisher
{
    private readonly ILogger<LoggingMessagePublisher> _logger;

    public LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        // Identifiers only (ticket #24): the body carries the user id and must not reach the logs.
        if (message is ReservationRequestMessage reservationRequest)
        {
            _logger.LogInformation(
                AvailabilityAuditEvents.ReservationRequestPublished,
                "Reservation event logged without a broker. Service: {Service}; MessageId: {MessageId}; CorrelationId: {CorrelationId}.",
                AvailabilityAuditEvents.ServiceName,
                reservationRequest.MessageId,
                reservationRequest.CorrelationId);
        }
        else
        {
            _logger.LogInformation("Event of type {MessageType} logged without a broker.", typeof(T).Name);
        }

        return Task.CompletedTask;
    }
}
