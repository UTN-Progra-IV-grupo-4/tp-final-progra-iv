using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Api.Reservations;

// Dispara periódicamente el caso de uso de expiración. Es un proceso por lotes y no un temporizador
// por reserva: no se pierde si el servicio se reinicia y varias réplicas pueden correrlo a la vez,
// porque cada transición es un UPDATE condicional en la base (ver ExpireRetainedReservations).
internal sealed class ExpiredReservationsBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<ExpiredReservationsBackgroundService> logger;
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;

    public ExpiredReservationsBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReservationProcessingOptions> options,
        ILogger<ExpiredReservationsBackgroundService> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var interval = options.Value.ExpirationCheckInterval;
        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ReservationProcessingOptions.ExpirationCheckInterval)} must be greater than zero.");
        }

        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.interval = interval;
        this.timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval, timeProvider);
            await ExecuteExpirationPassAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteExpirationPassAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExecuteExpirationPassAsync(CancellationToken stoppingToken)
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
                    "Expired {ProcessedCount} retained reservations at {ExecutedAtUtc}.",
                    result.ProcessedCount,
                    result.ExecutedAtUtc);
                logger.LogDebug(
                    "Expired reservation IDs: {ReservationIds}",
                    result.ReservationIds);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A transient failure in one pass must not stop future expiration checks.
            logger.LogError(
                exception,
                "Failed to expire retained reservations. The worker will retry at the next interval.");
        }
    }
}
