using Availability.Application.Events;

namespace Availability.Infrastructure.Events;

public class InMemoryEventCatalog : IEventCatalog
{
    private readonly IReadOnlyList<CatalogEvent> events = DemoCatalogData.CreateEvents();

    public Task<IReadOnlyList<EventSummary>> GetEventsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<EventSummary> result = events.Select(e =>
            new EventSummary(e.Id, e.Name, e.StartsAt, e.Seats.Count)).ToList();
        return Task.FromResult(result);
    }

    public Task<CatalogEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(events.SingleOrDefault(e => e.Id == eventId));
    }
}
