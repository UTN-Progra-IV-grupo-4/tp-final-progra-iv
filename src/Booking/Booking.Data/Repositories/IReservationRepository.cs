using Booking.Data.Models;

namespace Booking.Data.Repositories;

public interface IReservationRepository
{
    Task<ReservationInsertResult> TryInsertAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default);
}
