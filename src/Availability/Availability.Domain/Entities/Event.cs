namespace Availability.Domain.Entities;

// Dominio anémico: describe los datos; los casos de uso pertenecen a Application.
public class Event
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}
