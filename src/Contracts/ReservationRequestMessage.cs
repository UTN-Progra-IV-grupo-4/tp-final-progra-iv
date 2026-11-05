using System.Text.Json.Serialization;

namespace Contracts;

// Contrato compartido entre microservicios: es el único acoplamiento entre Availability (que lo
// publica) y Booking (que lo consume); ningún servicio referencia las clases internas del otro.
// MessageId identifica la solicitud y habilita la idempotencia de Booking ante reentregas.
// CorrelationId viaja con el mensaje para poder seguir una misma solicitud en los logs de los tres
// servicios.
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
