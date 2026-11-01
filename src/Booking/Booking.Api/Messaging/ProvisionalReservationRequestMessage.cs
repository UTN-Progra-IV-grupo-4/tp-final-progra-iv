using System.Text.Json.Serialization;

namespace Booking.Api.Messaging;

internal sealed record ProvisionalReservationRequestMessage(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("correlationId")] string? CorrelationId,
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("seatId")] Guid SeatId,
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("requestedAt")] DateTime RequestedAt);
