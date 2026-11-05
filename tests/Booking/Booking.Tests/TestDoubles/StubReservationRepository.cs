using Booking.Data.Models;
using Booking.Data.Repositories;

namespace Booking.Tests.TestDoubles;

public sealed class StubReservationRepository : IReservationRepository
{
    private readonly Queue<ReservationInsertResult> results = new();
    private readonly List<Reservation> attemptedReservations = [];
    private readonly Dictionary<Guid, Reservation> storedReservations = [];
    private readonly List<Reservation> expiredReservations = [];
    private readonly HashSet<Guid> expirationFailures = [];

    public IReadOnlyList<Reservation> AttemptedReservations => attemptedReservations;
    public List<int> ExpirationBatchSizes { get; } = [];
    public List<(Guid ReservationId, DateTimeOffset Now)> ExpirationAttempts { get; } = [];
    public List<(Guid EventId, Guid SeatId, DateTimeOffset Now)> SeatExpirationAttempts { get; } = [];
    public Exception? PaymentTransitionException { get; set; }
    public Dictionary<Guid, Reservation> ReservationsById => storedReservations;
    public Dictionary<(Guid EventId, DateTimeOffset Now), IReadOnlyList<Guid>>
        OccupiedSeatsByQuery
    { get; } = [];
    public void EnqueueResult(ReservationInsertResult result) => results.Enqueue(result);

    public void Seed(Reservation reservation) =>
        storedReservations[reservation.Id] = Clone(reservation);

    public Task<Reservation?> GetByIdAsync(
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(storedReservations.TryGetValue(reservationId, out var reservation)
            ? Clone(reservation)
            : null);
    }

    public void AddExpiredReservation(Reservation reservation, bool expirationSucceeds = true)
    {
        expiredReservations.Add(reservation);
        if (!expirationSucceeds)
        {
            expirationFailures.Add(reservation.Id);
        }
    }

    public Task<IReadOnlyList<Guid>> GetOccupiedSeatIdsAsync(
        Guid eventId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OccupiedSeatsByQuery.TryGetValue((eventId, now), out var seatIds);
        return Task.FromResult<IReadOnlyList<Guid>>(seatIds ?? []);
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
        var result = results.Dequeue();
        if (result.Outcome == ReservationInsertOutcome.Created)
        {
            Seed(reservation);
        }

        return Task.FromResult(result);
    }

    public Task<bool> TrySetPaymentOutcomeAsync(
        Guid reservationId,
        ReservationStatus status,
        ReservationRejectionReason? rejectionReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PaymentTransitionException is not null)
        {
            throw PaymentTransitionException;
        }

        if (!storedReservations.TryGetValue(reservationId, out var reservation) ||
            reservation.Status != ReservationStatus.Retained ||
            reservation.ExpiresAt <= utcNow)
        {
            return Task.FromResult(false);
        }

        reservation.Status = status;
        reservation.RejectionReason = rejectionReason;
        reservation.ExpiresAt = null;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Reservation>> GetExpiredReservationsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExpirationBatchSizes.Add(batchSize);
        IReadOnlyList<Reservation> reservations = expiredReservations
            .Where(reservation =>
                reservation.Status == ReservationStatus.Retained &&
                reservation.ExpiresAt <= now)
            .OrderBy(reservation => reservation.ExpiresAt)
            .ThenBy(reservation => reservation.Id)
            .Take(batchSize)
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
        if (reservation is null)
        {
            storedReservations.TryGetValue(reservationId, out reservation);
        }
        if (reservation is null ||
            reservation.Status != ReservationStatus.Retained ||
            reservation.ExpiresAt > now)
        {
            return Task.FromResult(false);
        }

        if (expirationFailures.Contains(reservationId))
        {
            reservation.Status = ReservationStatus.Confirmed;
            return Task.FromResult(false);
        }

        reservation.Status = ReservationStatus.Expired;
        return Task.FromResult(true);
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
