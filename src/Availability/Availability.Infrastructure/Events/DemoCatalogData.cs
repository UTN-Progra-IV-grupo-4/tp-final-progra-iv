using Availability.Application.Events;

namespace Availability.Infrastructure.Events;

// IDs iguales al seed SQL. Los seis asientos originales mantienen sus IDs.
public static class DemoCatalogData
{
    public static Guid EventId(int eventNumber)
        => Guid.Parse($"10000000-0000-0000-0000-{eventNumber:D12}");

    public static Guid SeatId(int eventNumber, int position)
    {
        var id = position <= 3
            ? (eventNumber - 1) * 3 + position
            : 6 + (eventNumber - 1) * 17 + position - 3;
        return Guid.Parse($"20000000-0000-0000-0000-{id:D12}");
    }

    public static IReadOnlyList<CatalogEvent> CreateEvents()
    {
        return new[]
        {
            CreateEvent(1, "Conferencia de tecnología", new DateTimeOffset(2030, 6, 15, 18, 0, 0, TimeSpan.Zero)),
            CreateEvent(2, "Recital de ejemplo", new DateTimeOffset(2030, 6, 20, 23, 0, 0, TimeSpan.Zero))
        };
    }

    private static CatalogEvent CreateEvent(int eventNumber, string name, DateTimeOffset startsAt)
    {
        var seats = Enumerable.Range(1, 20)
            .Select(position => new CatalogSeat(SeatId(eventNumber, position),
                position <= 10 ? $"A{position}" : $"B{position - 10}"))
            .ToList();
        return new CatalogEvent(EventId(eventNumber), name, startsAt, seats);
    }
}
