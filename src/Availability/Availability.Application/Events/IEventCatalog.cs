namespace Availability.Application.Events;

// La misma consulta puede usar memoria para la demo o PostgreSQL para el catálogo real.
public interface IEventCatalog
{
    Task<IReadOnlyList<EventSummary>> GetEventsAsync(CancellationToken cancellationToken);
    Task<CatalogEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken);
}
