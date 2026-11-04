using Availability.Application.Events;
using Availability.Infrastructure.Events;

namespace Availability.Tests;

public class EventQueryTests
{
    [Fact]
    public async Task Demo_ContainsTwoEventsWithTwentySeatsEach()
    {
        var queries = CreateQueries(new DemoSeatOccupancyQuery());
        var events = await queries.GetEventsAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(20, e.SeatCount));

        foreach (var item in events)
        {
            var result = await queries.GetSeatsAsync(item.Id);
            Assert.NotNull(result);
            Assert.True(result.OccupancyConfirmed);
            Assert.Equal(2, result.Seats.Count(s => s.Availability == SeatAvailability.Occupied));
            Assert.Equal(18, result.Seats.Count(s => s.Availability == SeatAvailability.Available));
        }
    }

    [Fact]
    public async Task MissingEvent_IsNotFoundWithoutConsultingOccupancy()
    {
        var occupancy = new FixedOccupancy(new SeatOccupancy(Guid.Empty, true, new HashSet<Guid>()));
        Assert.Null(await CreateQueries(occupancy).GetSeatsAsync(Guid.NewGuid()));
        Assert.Equal(0, occupancy.Calls);
    }

    [Fact]
    public async Task UnavailableOccupancy_DoesNotReportFreeSeats()
    {
        var result = await CreateQueries(new UnconfirmedSeatOccupancyStub())
            .GetSeatsAsync(DemoCatalogData.EventId(1));
        Assert.NotNull(result);
        Assert.False(result.OccupancyConfirmed);
        Assert.All(result.Seats, seat => Assert.Equal(SeatAvailability.Unknown, seat.Availability));
    }

    [Fact]
    public async Task ResponseForAnotherEvent_IsTreatedAsUnknown()
    {
        var occupancy = new FixedOccupancy(new SeatOccupancy(DemoCatalogData.EventId(2), true, new HashSet<Guid>()));
        var result = await CreateQueries(occupancy).GetSeatsAsync(DemoCatalogData.EventId(1));
        Assert.NotNull(result);
        Assert.False(result.OccupancyConfirmed);
        Assert.All(result.Seats, seat => Assert.Equal(SeatAvailability.Unknown, seat.Availability));
    }

    [Fact]
    public async Task ConfirmedEmptyOccupancy_MeansAllCatalogSeatsAreFree()
    {
        var id = DemoCatalogData.EventId(1);
        var result = await CreateQueries(new FixedOccupancy(new SeatOccupancy(id, true, new HashSet<Guid>())))
            .GetSeatsAsync(id);
        Assert.NotNull(result);
        Assert.True(result.OccupancyConfirmed);
        Assert.All(result.Seats, seat => Assert.Equal(SeatAvailability.Available, seat.Availability));
        Assert.Equal(new[] { "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9", "A10" },
            result.Seats.Where(s => s.Number.StartsWith('A')).Select(s => s.Number));
    }

    private static EventQueryService CreateQueries(ISeatOccupancyQuery occupancy)
        => new(new InMemoryEventCatalog(), occupancy);

    private sealed class FixedOccupancy(SeatOccupancy response) : ISeatOccupancyQuery
    {
        public int Calls { get; private set; }
        public Task<SeatOccupancy> GetOccupiedSeatsAsync(Guid eventId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response);
        }
    }
}
