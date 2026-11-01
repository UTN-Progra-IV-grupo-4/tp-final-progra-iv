using Booking.Data.Models;

namespace Booking.Data.Repositories;

public enum ReservationInsertOutcome
{
    Created,
    RequestIdExists,
    SeatTaken
}

public sealed record ReservationInsertResult(
    ReservationInsertOutcome Outcome,
    Reservation? ExistingReservation = null)
{
    public static ReservationInsertResult Created() =>
        new(ReservationInsertOutcome.Created);

    public static ReservationInsertResult RequestIdExists(Reservation existingReservation) =>
        new(ReservationInsertOutcome.RequestIdExists, existingReservation);

    public static ReservationInsertResult SeatTaken() =>
        new(ReservationInsertOutcome.SeatTaken);
}
