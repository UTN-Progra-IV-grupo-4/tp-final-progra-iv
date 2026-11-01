using Availability.Web.Abstractions;
using Availability.Web.Domain;
using Contracts;

namespace Availability.Web.Application;

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
            EventId = command.EventId,
            UserId = userId,
            Status = ReservationRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(reservationRequest, cancellationToken);

        var message = new ReservationRequestMessage
        {
            MessageId = reservationRequest.Id,
            CorrelationId = reservationRequest.Id.ToString(),
            EventId = reservationRequest.EventId,
            SeatId = reservationRequest.SeatId,
            UserId = reservationRequest.UserId,
            RequestedAt = reservationRequest.CreatedAt
        };

        await _messagePublisher.PublishAsync(message, cancellationToken);

        return reservationRequest;
    }
}
