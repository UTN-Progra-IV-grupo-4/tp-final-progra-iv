using Availability.Application.Abstractions;
using Availability.Application.Logging;
using Availability.Domain.Entities;
using Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Application.ReservationRequests;

// Caso de uso: registra la solicitud de reserva y la publica al broker para que Booking la procese.
// La solicitud no es una reserva: Booking decide si el asiento se retiene o se rechaza.
public class ReservationRequestHandler
{
    private readonly IReservationRequestRepository _repository;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<ReservationRequestHandler> _logger;

    // The logger is optional so callers that only care about the use case need not build one; the
    // container always supplies the real one.
    public ReservationRequestHandler(
        IReservationRequestRepository repository,
        IMessagePublisher messagePublisher,
        ILogger<ReservationRequestHandler>? logger = null)
    {
        _repository = repository;
        _messagePublisher = messagePublisher;
        _logger = logger ?? NullLogger<ReservationRequestHandler>.Instance;
    }

    public async Task<ReservationRequest> HandleAsync(
        ReservationRequestCommand command,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("Authenticated user is required to create a reservation request.");
        }

        var seatExists = await _repository.SeatExistsAsync(command.SeatId, command.EventId, cancellationToken);
        if (!seatExists)
        {
            throw new InvalidOperationException("Seat does not exist for the requested event.");
        }

        var reservationRequest = new ReservationRequest
        {
            Id = Guid.NewGuid(),
            SeatId = command.SeatId,
            UserId = userId,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(reservationRequest, cancellationToken);

        // The request id doubles as the correlation id for the whole chain (broker message, Booking, Payment).
        // Start of the chain; the user id is personal data and stays out of the audit line.
        var correlationId = reservationRequest.Id.ToString();
        _logger.LogInformation(
            AvailabilityAuditEvents.ReservationRequestStored,
            "Reservation request stored. Service: {Service}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; EventId: {EventId}; SeatId: {SeatId}.",
            AvailabilityAuditEvents.ServiceName,
            reservationRequest.Id,
            correlationId,
            command.EventId,
            reservationRequest.SeatId);

        var message = new ReservationRequestMessage
        {
            MessageId = reservationRequest.Id,
            CorrelationId = correlationId,
            EventId = command.EventId,
            SeatId = reservationRequest.SeatId,
            UserId = reservationRequest.UserId,
            RequestedAt = reservationRequest.RequestedAt.UtcDateTime
        };

        try
        {
            await _messagePublisher.PublishAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            // Decisión: compensar en lugar de usar un outbox. Guardar y publicar no son atómicos, así
            // que si el publish falla puede quedar una solicitud registrada sin publicar; se la borra
            // para que Availability no la muestre como "en proceso" para siempre, ya que Booking
            // nunca se enteraría de ella. Un outbox lo evitaría, pero agrega una tabla y un proceso
            // de reenvío que excede el alcance del proyecto.
            // CancellationToken.None: a cancelled caller must not leave the cleanup half done.
            await _repository.RemoveAsync(reservationRequest, CancellationToken.None);

            // A cancellation is not a broker failure: rethrow it untouched instead of wrapping it.
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new ReservationRequestPublishException(reservationRequest.Id, exception);
        }

        return reservationRequest;
    }
}
