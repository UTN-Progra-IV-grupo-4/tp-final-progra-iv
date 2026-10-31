using Booking.Api.Reservations;
using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Reservations;

public sealed class ExpiredReservationsBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenUseCaseFails_ContinuesAtNextInterval()
    {
        var useCase = new FailingThenSucceedingExpireUseCase();
        var services = new ServiceCollection();
        services.AddScoped<IExpireRetainedReservations>(_ => useCase);
        await using var serviceProvider = services.BuildServiceProvider();
        var worker = new ExpiredReservationsBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReservationProcessingOptions
            {
                ExpirationCheckInterval = TimeSpan.FromMilliseconds(20)
            }),
            NullLogger<ExpiredReservationsBackgroundService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await useCase.TwoExecutions.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, useCase.ExecutionCount);
    }

    private sealed class FailingThenSucceedingExpireUseCase : IExpireRetainedReservations
    {
        private readonly TaskCompletionSource twoExecutions =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExecutionCount { get; private set; }

        public Task TwoExecutions => twoExecutions.Task;

        public Task<ExpireRetainedReservationsResult> ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            if (ExecutionCount == 1)
            {
                throw new InvalidOperationException("Expected test failure.");
            }

            twoExecutions.TrySetResult();
            return Task.FromResult(new ExpireRetainedReservationsResult(
                0,
                [],
                DateTimeOffset.UtcNow));
        }
    }
}
