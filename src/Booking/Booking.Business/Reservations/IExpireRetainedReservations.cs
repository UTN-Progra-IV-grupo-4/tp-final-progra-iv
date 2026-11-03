namespace Booking.Business.Reservations;

public interface IExpireRetainedReservations
{
    Task<ExpireRetainedReservationsResult> ExecuteAsync(
        CancellationToken cancellationToken = default);
}
