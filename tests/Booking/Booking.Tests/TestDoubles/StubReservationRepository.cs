using Booking.Data.Models;
using Booking.Data.Repositories;

namespace Booking.Tests.TestDoubles;

public sealed class StubReservationRepository : IReservationRepository
{
    private readonly Queue<ReservationInsertResult> results = new();
    private readonly List<Reservation> attemptedReservations = [];
    private readonly List<Reservation> expiredReservations = [];
    private readonly HashSet<Guid> expirationFailures = [];

    public IReadOnlyList<Reservation> AttemptedReservations => attemptedReservations;
    public List<(Guid ReservationId, DateTimeOffset Now)> ExpirationAttempts { get; } = [];
    public List<(Guid EventId, Guid SeatId, DateTimeOffset Now)> SeatExpirationAttempts { get; } = [];

    public void EnqueueResult(ReservationInsertResult result) => results.Enqueue(result);

    public void AddExpiredReservation(Reservation reservation, bool expirationSucceeds = true)
    {
        expiredReservations.Add(reservation);
        if (!expirationSucceeds)
        {
            expirationFailures.Add(reservation.Id);
        }
    }

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
        IReadOnlyList<Reservation> reservations = expiredReservations
            .Where(reservation =>
                reservation.Status == ReservationStatus.Retained &&
                reservation.ExpiresAt <= now)
            .ToList();
        return Task.FromResult(reservations);
    }

    public Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExpirationAttempts.Add((reservationId, now));
        var reservation = expiredReservations.SingleOrDefault(
            candidate => candidate.Id == reservationId);
        return Task.FromResult(
            reservation is not null &&
            reservation.Status == ReservationStatus.Retained &&
            reservation.ExpiresAt <= now &&
            !expirationFailures.Contains(reservationId));
    }

    public Task<bool> TryExpireRetainedReservationForSeatAsync(
        Guid eventId,
        Guid seatId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SeatExpirationAttempts.Add((eventId, seatId, now));
        var reservation = expiredReservations.SingleOrDefault(candidate =>
            candidate.EventId == eventId &&
            candidate.SeatId == seatId &&
            candidate.Status == ReservationStatus.Retained &&
            candidate.ExpiresAt <= now);
        if (reservation is null || expirationFailures.Contains(reservation.Id))
        {
            return Task.FromResult(false);
        }

        reservation.Status = ReservationStatus.Expired;
        return Task.FromResult(true);
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
