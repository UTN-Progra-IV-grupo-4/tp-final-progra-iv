namespace Booking.Business.Reservations;

public sealed record ProcessReservationRequest(
    Guid RequestId,
    Guid EventId,
    Guid SeatId);
