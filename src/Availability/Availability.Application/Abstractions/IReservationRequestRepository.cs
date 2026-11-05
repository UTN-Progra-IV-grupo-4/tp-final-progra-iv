using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

public interface IReservationRequestRepository
{
    Task<bool> SeatExistsAsync(Guid seatId, Guid eventId, CancellationToken cancellationToken = default);
    Task AddAsync(ReservationRequest reservationRequest, CancellationToken cancellationToken = default);
    Task RemoveAsync(ReservationRequest reservationRequest, CancellationToken cancellationToken = default);
    Task<ReservationRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken = default);
}
