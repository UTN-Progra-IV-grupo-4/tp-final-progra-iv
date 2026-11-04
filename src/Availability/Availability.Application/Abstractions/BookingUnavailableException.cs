namespace Availability.Application.Abstractions;
// Raised when Booking could not report the status of a request: it failed (5xx), rejected the
// service token (401/403) or could not be reached. It does not mean the request was rejected.
public class BookingUnavailableException(Guid requestId, Exception? innerException = null)
    : Exception($"Booking could not report the status of reservation request {requestId}.", innerException)
{
    public Guid RequestId { get; } = requestId;
}