using Booking.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Booking.Data.Repositories;

public sealed class ReservationRepository(BookingDbContext dbContext) : IReservationRepository
{
    private const string SeatReservationConstraint = "IX_reservations_event_id_seat_id";

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

    public async Task<IReadOnlyList<Reservation>> GetExpiredReservationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await dbContext.Reservations
            .AsNoTracking()
            .Where(reservation =>
                reservation.Status == ReservationStatus.Retained &&
                reservation.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var updatedCount = await dbContext.Reservations
            .Where(reservation =>
                reservation.Id == reservationId &&
                reservation.Status == ReservationStatus.Retained &&
                reservation.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    reservation => reservation.Status,
                    ReservationStatus.Expired),
                cancellationToken);

        return updatedCount > 0;
    }

    private static PostgresException? GetUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            ? postgresException
            : null;
}
