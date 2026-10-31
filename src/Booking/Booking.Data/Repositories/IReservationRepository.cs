using Booking.Data.Models;

namespace Booking.Data.Repositories;

public interface IReservationRepository
{
    Task<ReservationInsertResult> TryInsertAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reservation>> GetExpiredReservationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
