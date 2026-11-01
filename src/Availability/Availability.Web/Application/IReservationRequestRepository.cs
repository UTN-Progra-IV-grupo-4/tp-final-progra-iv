using Availability.Web.Domain;

namespace Availability.Web.Application;

public interface IReservationRequestRepository
{
    Task<bool> SeatExistsAsync(Guid seatId, Guid eventId, CancellationToken cancellationToken = default);
    Task AddAsync(ReservationRequest reservationRequest, CancellationToken cancellationToken = default);
}
