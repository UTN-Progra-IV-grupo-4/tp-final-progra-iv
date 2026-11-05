using Availability.Application.Events;

namespace Availability.Tests;

// Doble de prueba: permite probar la presentación sin acoplarla al transporte HTTP.
internal sealed class UnconfirmedSeatOccupancyStub : ISeatOccupancyQuery
{
    public Task<SeatOccupancy> GetOccupiedSeatsAsync(Guid eventId, CancellationToken cancellationToken)
        => Task.FromResult(new SeatOccupancy(eventId, false, new HashSet<Guid>()));
}
