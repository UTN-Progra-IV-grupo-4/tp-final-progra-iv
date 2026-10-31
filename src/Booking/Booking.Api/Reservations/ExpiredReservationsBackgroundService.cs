using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Api.Reservations;

internal sealed class ExpiredReservationsBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<ExpiredReservationsBackgroundService> logger;
    private readonly TimeSpan interval;

    public ExpiredReservationsBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ExpiredReservationsBackgroundServiceOptions> options,
        ILogger<ExpiredReservationsBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        interval = options.Value.Interval;
        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ExpiredReservationsBackgroundServiceOptions.Interval)} must be greater than zero.");
        }

        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var useCase = scope.ServiceProvider
                    .GetRequiredService<IExpireRetainedReservations>();
                var result = await useCase.ExecuteAsync(stoppingToken);

                if (result.ProcessedCount > 0)
                {
                    logger.LogInformation(
                        "Expired {ProcessedCount} retained reservations at {ExecutedAtUtc}. Reservation IDs: {ReservationIds}",
                        result.ProcessedCount,
                        result.ExecutedAtUtc,
                        result.ReservationIds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to expire retained reservations. The worker will retry at the next interval.");
            }
        }
    }
}
