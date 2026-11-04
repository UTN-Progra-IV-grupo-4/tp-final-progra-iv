namespace Availability.Application.Abstractions;
public interface IBookingStatusClient
{
    /// <summary>
    /// Asks Booking for the status of a reservation request.
    /// Returns null when Booking answers 404 (not consumed yet: "processing").
    /// Throws BookingUnavailableException when Booking fails or cannot be reached.
    /// </summary>
    Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default);
}