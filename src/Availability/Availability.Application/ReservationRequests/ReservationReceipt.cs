namespace Availability.Application.ReservationRequests;

// What the "request received" screen shows. A received request is not a confirmed reservation:
// Booking decides the outcome later.
public record ReservationReceipt(
    Guid RequestId,
    string EventName,
    DateTimeOffset EventStartsAt,
    string SeatNumber,
    DateTimeOffset RequestedAt);
