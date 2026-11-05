using Availability.Application.Abstractions;

namespace Availability.Application.ReservationRequests;

public class ReservationReceiptHandler(IReservationRequestRepository repository)
{
    // Throws KeyNotFoundException when the request does not exist or belongs to another user, with
    // the same answer for both so other users' request ids are not revealed.
    public async Task<ReservationReceipt> HandleAsync(
        Guid requestId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var reservationRequest = await repository.GetByIdWithSeatAsync(requestId, cancellationToken);
        if (reservationRequest is null || reservationRequest.UserId != userId)
        {
            throw new KeyNotFoundException($"Reservation request {requestId} was not found.");
        }

        return new ReservationReceipt(
            reservationRequest.Id,
            reservationRequest.Seat.Event.Name,
            reservationRequest.Seat.Event.StartsAt,
            reservationRequest.Seat.Number,
            reservationRequest.RequestedAt);
    }
}
