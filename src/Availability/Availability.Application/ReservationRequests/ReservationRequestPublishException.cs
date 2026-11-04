namespace Availability.Application.ReservationRequests;

// Raised when the request could not be handed to the broker. The stored request has already
// been removed, so the caller can safely retry with a new request.
public class ReservationRequestPublishException(Guid requestId, Exception innerException)
    : Exception($"Reservation request {requestId} could not be published.", innerException)
{
    public Guid RequestId { get; } = requestId;
}
