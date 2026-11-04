using Availability.Application.Events;

namespace Availability.Infrastructure.Events;

// TODO (#21/#11): reemplazar por el cliente de Booking autenticado con availability-service.
// Hasta integrar el endpoint, el catálogo real no afirma que los asientos estén libres.
public class UnavailableSeatOccupancyQuery : ISeatOccupancyQuery
{
    public Task<SeatOccupancy> GetOccupiedSeatsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SeatOccupancy(eventId, false, new HashSet<Guid>()));
    }
}
