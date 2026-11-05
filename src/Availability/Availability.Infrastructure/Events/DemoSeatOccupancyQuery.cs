using Availability.Application.Events;

namespace Availability.Infrastructure.Events;

// Solo para desarrollo: A2 y B3 aparecen ocupados en cada evento.
// No representa reservas reales ni consume mensajes del broker.
public class DemoSeatOccupancyQuery : ISeatOccupancyQuery
{
    public Task<SeatOccupancy> GetOccupiedSeatsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eventNumber = eventId == DemoCatalogData.EventId(1) ? 1 :
            eventId == DemoCatalogData.EventId(2) ? 2 : 0;
        IReadOnlySet<Guid> occupied = eventNumber == 0 ? new HashSet<Guid>() :
            new HashSet<Guid> { DemoCatalogData.SeatId(eventNumber, 2), DemoCatalogData.SeatId(eventNumber, 13) };
        return Task.FromResult(new SeatOccupancy(eventId, eventNumber != 0, occupied));
    }
}
