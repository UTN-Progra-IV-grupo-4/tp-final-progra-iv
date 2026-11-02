using Booking.Data.Models;

namespace Booking.Data.Repositories;

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(
        Guid reservationId,
        CancellationToken cancellationToken = default);

    Task<ReservationInsertResult> TryInsertAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default);

    Task<bool> TrySetPaymentOutcomeAsync(
        Guid reservationId,
        ReservationStatus status,
        ReservationRejectionReason? rejectionReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
