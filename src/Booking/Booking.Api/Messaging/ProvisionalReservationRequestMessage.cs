using System.Text.Json.Serialization;

namespace Booking.Api.Messaging;

internal sealed record ProvisionalReservationRequestMessage(
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("seatId")] Guid SeatId);
