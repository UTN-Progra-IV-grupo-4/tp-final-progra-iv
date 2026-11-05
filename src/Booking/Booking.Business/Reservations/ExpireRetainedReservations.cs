using Booking.Data.Repositories;
using Microsoft.Extensions.Options;

namespace Booking.Business.Reservations;

// La expiración es un proceso periódico por lotes y no un temporizador por reserva: no depende de
// memoria del proceso (sobrevive a reinicios) y se acota con el tamaño de lote. Cada transición es
// un UPDATE condicional (sigue Retained y vencida), así que varias réplicas pueden ejecutarlo a la
// vez sin expirar dos veces la misma reserva ni pisar una confirmación de pago.
public sealed class ExpireRetainedReservations : IExpireRetainedReservations
{
    private readonly IReservationRepository reservationRepository;
    private readonly TimeProvider timeProvider;
    private readonly int batchSize;

    public ExpireRetainedReservations(
        IReservationRepository reservationRepository,
        TimeProvider timeProvider,
        IOptions<ReservationProcessingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(reservationRepository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Value.ExpirationBatchSize <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(ReservationProcessingOptions.ExpirationBatchSize)} must be greater than zero.");
        }

        this.reservationRepository = reservationRepository;
        this.timeProvider = timeProvider;
        batchSize = options.Value.ExpirationBatchSize;
    }

    public async Task<ExpireRetainedReservationsResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var executedAtUtc = timeProvider.GetUtcNow();
        var reservationIds = new List<Guid>();
        while (true)
        {
            var expiredReservations = await reservationRepository.GetExpiredReservationsAsync(
                executedAtUtc,
                batchSize,
                cancellationToken);

            if (expiredReservations.Count == 0)
            {
                break;
            }

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
        }

        return new ExpireRetainedReservationsResult(
            reservationIds.Count,
            reservationIds,
            executedAtUtc);
    }
}
