namespace Availability.Application.Events;

public interface ISeatOccupancyQuery
{
    Task<SeatOccupancy> GetOccupiedSeatsAsync(Guid eventId, CancellationToken cancellationToken);
}

// IsConfirmed=false significa que no conocemos la ocupación. No equivale a lista vacía.
public record SeatOccupancy(Guid EventId, bool IsConfirmed, IReadOnlySet<Guid> SeatIds);
