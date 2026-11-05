using Availability.Application.Events;
using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Events;

public class EfEventCatalog(AvailabilityDbContext context) : IEventCatalog
{
    public async Task<IReadOnlyList<EventSummary>> GetEventsAsync(CancellationToken cancellationToken)
    {
        return await context.Events.AsNoTracking().OrderBy(e => e.StartsAt).ThenBy(e => e.Name)
            .Select(e => new EventSummary(e.Id, e.Name, e.StartsAt, e.Seats.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<CatalogEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventData = await context.Events.AsNoTracking().Include(e => e.Seats)
            .SingleOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        return eventData is null ? null : new CatalogEvent(eventData.Id, eventData.Name,
            eventData.StartsAt, eventData.Seats.Select(s => new CatalogSeat(s.Id, s.Number)).ToList());
    }
}
