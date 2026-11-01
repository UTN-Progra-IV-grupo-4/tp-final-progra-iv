using Availability.Web.Application;
using Availability.Web.Domain;

namespace Availability.Web.Infrastructure;

public class InMemoryReservationRequestRepository : IReservationRequestRepository
{
    private readonly List<ReservationRequest> _requests = [];

    public Task<bool> SeatExistsAsync(Guid seatId, Guid eventId, CancellationToken cancellationToken = default)
    {
        var exists = true;

        // Mock validation: in a real implementation this would check the DB or an availability service.
        if (seatId == Guid.Empty || eventId == Guid.Empty)
        {
            exists = false;
        }

        return Task.FromResult(exists);
    }

    public Task AddAsync(ReservationRequest reservationRequest, CancellationToken cancellationToken = default)
    {
        _requests.Add(reservationRequest);
        return Task.CompletedTask;
    }
}
