namespace Booking.Business.Reservations;

public sealed record ExpireRetainedReservationsResult(
    int ProcessedCount,
    IReadOnlyList<Guid> ReservationIds,
    DateTimeOffset ExecutedAtUtc);
