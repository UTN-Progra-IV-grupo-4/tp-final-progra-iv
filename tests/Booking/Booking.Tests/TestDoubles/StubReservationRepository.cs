using Booking.Data.Models;
using Booking.Data.Repositories;

namespace Booking.Tests.TestDoubles;

public sealed class StubReservationRepository : IReservationRepository
{
    private readonly Queue<ReservationInsertResult> results = new();
    private readonly List<Reservation> attemptedReservations = [];
    private readonly Dictionary<Guid, Reservation> storedReservations = [];

    public IReadOnlyList<Reservation> AttemptedReservations => attemptedReservations;

    public Exception? PaymentTransitionException { get; set; }

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

    public Task<bool> TryExpireAsync(
        Guid reservationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!storedReservations.TryGetValue(reservationId, out var reservation) ||
            reservation.Status != ReservationStatus.Retained ||
            reservation.ExpiresAt > utcNow)
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
