using Booking.Data.Models;

namespace Booking.Data.Repositories;

// State transitions must be conditional updates; payment confirmation must also require ExpiresAt > now.
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

    Task<IReadOnlyList<Reservation>> GetExpiredReservationsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> TryExpireRetainedReservationForSeatAsync(
        Guid eventId,
        Guid seatId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
