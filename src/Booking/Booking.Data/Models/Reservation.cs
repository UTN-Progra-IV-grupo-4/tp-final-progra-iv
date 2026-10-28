namespace Booking.Data.Models;

public class Reservation
{
    public Guid Id { get; set; }

    public Guid SeatId { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Retained;

    public ReservationRejectionReason? RejectionReason { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
