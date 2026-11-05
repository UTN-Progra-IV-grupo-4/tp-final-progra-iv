namespace Availability.Domain.Entities;

// Dominio anémico: el asiento solo describe datos. Si está libre u ocupado lo decide Booking.
public class Seat
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Event Event { get; set; } = null!;

    // No guardamos IsAvailable: Booking administra las reservas y su ocupación.
}
