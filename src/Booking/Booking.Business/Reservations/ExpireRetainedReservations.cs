using Booking.Data.Repositories;

namespace Booking.Business.Reservations;

public sealed class ExpireRetainedReservations : IExpireRetainedReservations
{
    private readonly IReservationRepository reservationRepository;
    private readonly TimeProvider timeProvider;

    public ExpireRetainedReservations(
        IReservationRepository reservationRepository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reservationRepository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.reservationRepository = reservationRepository;
        this.timeProvider = timeProvider;
    }

    public async Task<ExpireRetainedReservationsResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var executedAtUtc = timeProvider.GetUtcNow();
        var expiredReservations = await reservationRepository.GetExpiredReservationsAsync(
            executedAtUtc,
            cancellationToken);
        var reservationIds = new List<Guid>(expiredReservations.Count);

        foreach (var reservation in expiredReservations)
        {
            var expired = await reservationRepository.TryExpireAsync(
                reservation.Id,
                executedAtUtc,
                cancellationToken);

            if (expired)
            {
                reservationIds.Add(reservation.Id);
            }
        }

        return new ExpireRetainedReservationsResult(
            reservationIds.Count,
            reservationIds,
            executedAtUtc);
    }
}
