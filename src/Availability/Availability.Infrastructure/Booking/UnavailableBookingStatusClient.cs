using Availability.Application.Abstractions;

namespace Availability.Infrastructure.Booking;

// Used outside Development when Booking:BaseUrl is not configured (the real BookingStatusClient
// is registered otherwise): it fails loudly instead of inventing a status for the user.
public class UnavailableBookingStatusClient : IBookingStatusClient
{
    public Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        throw new BookingUnavailableException(
            requestId,
            new InvalidOperationException("Booking:BaseUrl is not configured."));
    }
}
