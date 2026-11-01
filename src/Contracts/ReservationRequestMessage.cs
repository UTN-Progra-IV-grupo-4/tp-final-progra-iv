namespace Contracts;

public class ReservationRequestMessage
{
    public Guid MessageId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public Guid SeatId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}
