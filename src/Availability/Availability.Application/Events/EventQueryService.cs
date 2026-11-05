namespace Availability.Application.Events;

public class EventQueryService(IEventCatalog catalog, ISeatOccupancyQuery occupancyQuery)
{
    public Task<IReadOnlyList<EventSummary>> GetEventsAsync(CancellationToken cancellationToken = default)
        => catalog.GetEventsAsync(cancellationToken);

    public async Task<EventSeats?> GetSeatsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var eventData = await catalog.GetEventAsync(eventId, cancellationToken);
        if (eventData is null)
        {
            return null;
        }

        var occupancy = await occupancyQuery.GetOccupiedSeatsAsync(eventId, cancellationToken);
        // Una respuesta para otro evento no sirve para afirmar disponibilidad.
        var confirmed = occupancy.IsConfirmed && occupancy.EventId == eventId;
        var seats = eventData.Seats
            .OrderBy(seat => seat.Number.Length)
            .ThenBy(seat => seat.Number, StringComparer.OrdinalIgnoreCase)
            .Select(seat => new SeatAvailabilityItem(seat.Id, seat.Number,
                !confirmed ? SeatAvailability.Unknown :
                occupancy.SeatIds.Contains(seat.Id) ? SeatAvailability.Occupied : SeatAvailability.Available))
            .ToList();

        return new EventSeats(eventData.Id, eventData.Name, eventData.StartsAt, confirmed, seats);
    }
}
