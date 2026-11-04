namespace Availability.Application.ReservationRequests;

public class ReservationRequestCommand
{
    public Guid EventId { get; set; }
    public Guid SeatId { get; set; }
}
