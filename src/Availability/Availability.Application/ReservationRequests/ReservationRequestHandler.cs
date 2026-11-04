using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Contracts;

namespace Availability.Application.ReservationRequests;

public class ReservationRequestHandler
{
    private readonly IReservationRequestRepository _repository;
    private readonly IMessagePublisher _messagePublisher;

    public ReservationRequestHandler(
        IReservationRequestRepository repository,
        IMessagePublisher messagePublisher)
    {
        _repository = repository;
        _messagePublisher = messagePublisher;
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

        var message = new ReservationRequestMessage
        {
            MessageId = reservationRequest.Id,
            CorrelationId = reservationRequest.Id.ToString(),
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
            // Without the message Booking never learns about the request, and Availability would
            // report it as "processing" forever, so the stored row is compensated (removed).
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
