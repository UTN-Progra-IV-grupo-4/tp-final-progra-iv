namespace Booking.Api.Messaging;

internal sealed record ReservationRequestDelivery(
    BinaryData Body,
    string? MessageId,
    string? CorrelationId,
    string? Subject,
    int DeliveryCount = 0);
