using Availability.Application.Abstractions;
namespace Availability.Application.ReservationRequests;
public class ReservationStatusHandler(
    IReservationRequestRepository repository,
    IBookingStatusClient bookingStatusClient)
{
    // Returns null while Booking has not consumed the request yet ("processing").
    // Throws KeyNotFoundException when the request does not exist or belongs to another user.
    // Throws BookingUnavailableException when Booking cannot answer.
    public async Task<ReservationStatusResult?> HandleAsync(
        Guid requestId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var reservationRequest = await repository.GetByIdAsync(requestId, cancellationToken);
        // Booking does not check ownership, so it is checked here before asking Booking.
        // Missing and foreign requests get the same answer: other users' ids are not revealed.
        if (reservationRequest is null || reservationRequest.UserId != userId)
        {
            throw new KeyNotFoundException($"Reservation request {requestId} was not found.");
        }
        return await bookingStatusClient.GetStatusAsync(requestId, cancellationToken);
    }
}