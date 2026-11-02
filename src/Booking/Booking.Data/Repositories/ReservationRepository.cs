using Booking.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Booking.Data.Repositories;

public sealed class ReservationRepository(BookingDbContext dbContext) : IReservationRepository
{
    private const string SeatReservationConstraint = "IX_reservations_event_id_seat_id";

    public Task<Reservation?> GetByIdAsync(
        Guid reservationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Reservations.AsNoTracking().SingleOrDefaultAsync(
            reservation => reservation.Id == reservationId,
            cancellationToken);

    public async Task<ReservationInsertResult> TryInsertAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        dbContext.Reservations.Add(reservation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ReservationInsertResult.Created();
        }
        catch (DbUpdateException exception) when (GetUniqueViolation(exception) is not null)
        {
            // SaveChanges leaves failed entries tracked. Detaching them makes the scoped
            // context safe for the query and for a possible Rejected/SeatTaken insert.
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            var existingReservation = await dbContext.Reservations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    existing => existing.Id == reservation.Id,
                    cancellationToken);

            if (existingReservation is not null)
            {
                return ReservationInsertResult.RequestIdExists(existingReservation);
            }

            var uniqueViolation = GetUniqueViolation(exception)!;
            if (uniqueViolation.ConstraintName == SeatReservationConstraint)
            {
                return ReservationInsertResult.SeatTaken();
            }

            throw;
        }
    }

    public async Task<bool> TrySetPaymentOutcomeAsync(
        Guid reservationId,
        ReservationStatus status,
        ReservationRejectionReason? rejectionReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (status is not (ReservationStatus.Confirmed or ReservationStatus.Rejected) ||
            (status == ReservationStatus.Confirmed && rejectionReason is not null) ||
            (status == ReservationStatus.Rejected && rejectionReason is not (
                ReservationRejectionReason.PaymentFailed or ReservationRejectionReason.PaymentUnavailable)))
        {
            throw new ArgumentException("A payment transition requires a valid terminal status and reason.");
        }

        // PostgreSQL applies this predicate and update atomically across Booking replicas.
        var updated = await dbContext.Reservations
            .Where(reservation => reservation.Id == reservationId &&
                reservation.Status == ReservationStatus.Retained &&
                reservation.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(reservation => reservation.Status, status)
                .SetProperty(reservation => reservation.RejectionReason, rejectionReason)
                .SetProperty(reservation => reservation.ExpiresAt, (DateTimeOffset?)null),
                cancellationToken);

        return updated == 1;
    }

    public async Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.Reservations
            .Where(reservation => reservation.Id == reservationId &&
                reservation.Status == ReservationStatus.Retained &&
                reservation.ExpiresAt <= utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(reservation => reservation.Status, ReservationStatus.Expired),
                cancellationToken);

        return updated == 1;
    }

    private static PostgresException? GetUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            ? postgresException
            : null;
}
