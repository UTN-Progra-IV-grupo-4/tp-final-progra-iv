namespace Availability.Domain.Entities;

public class Seat
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Event Event { get; set; } = null!;

    // No guardamos IsAvailable: Booking administra las reservas y su ocupación.
}
