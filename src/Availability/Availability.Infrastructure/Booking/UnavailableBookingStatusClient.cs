using Availability.Application.Abstractions;

namespace Availability.Infrastructure.Booking;

// Used outside Development until the real HTTP client against Booking (#21) exists:
// it fails loudly instead of inventing a status for the user.
public class UnavailableBookingStatusClient : IBookingStatusClient
{
    public Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        throw new BookingUnavailableException(
            requestId,
            new InvalidOperationException("El cliente real de Booking todavía no está configurado."));
    }
}
