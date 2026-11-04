using System.Text.Json.Serialization;

namespace Contracts;

public class ReservationRequestMessage
{
    [JsonPropertyName("messageId")]
    public Guid MessageId { get; set; }

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; }

    [JsonPropertyName("seatId")]
    public Guid SeatId { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}
