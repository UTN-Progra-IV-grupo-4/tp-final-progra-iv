namespace Booking.Business.Reservations;

public interface IReservationRequestProcessor
{
    Task<ReservationProcessingResult> ProcessAsync(
        ProcessReservationRequest request,
        CancellationToken cancellationToken = default);
}
