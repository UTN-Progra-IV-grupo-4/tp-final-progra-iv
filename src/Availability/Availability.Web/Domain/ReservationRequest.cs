namespace Availability.Web.Domain;

public enum ReservationRequestStatus
{
    Pending = 0
}

public class ReservationRequest
{
    public Guid Id { get; set; }
    public Guid SeatId { get; set; }
    public Guid EventId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ReservationRequestStatus Status { get; set; } = ReservationRequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
