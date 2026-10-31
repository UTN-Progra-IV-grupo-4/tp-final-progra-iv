using Booking.Data.Models;
using Booking.Data.Repositories;

namespace Booking.Tests.TestDoubles;

public sealed class StubReservationRepository : IReservationRepository
{
    private readonly Queue<ReservationInsertResult> results = new();
    private readonly List<Reservation> attemptedReservations = [];

    public IReadOnlyList<Reservation> AttemptedReservations => attemptedReservations;

    public void EnqueueResult(ReservationInsertResult result) => results.Enqueue(result);

    public Task<ReservationInsertResult> TryInsertAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (results.Count == 0)
        {
            throw new InvalidOperationException("No repository result was configured for this call.");
        }

        attemptedReservations.Add(Clone(reservation));
        return Task.FromResult(results.Dequeue());
    }

    public Task<IReadOnlyList<Reservation>> GetExpiredReservationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Reservation>>([]);
    }

    public Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private static Reservation Clone(Reservation reservation) =>
        new()
        {
            Id = reservation.Id,
            EventId = reservation.EventId,
            SeatId = reservation.SeatId,
            Status = reservation.Status,
            RejectionReason = reservation.RejectionReason,
            ExpiresAt = reservation.ExpiresAt
        };
}
