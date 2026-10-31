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

    private static PostgresException? GetUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            ? postgresException
            : null;
}
