using Booking.Api.Reservations;
using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

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
        var timeProvider = new FakeTimeProvider();
        var worker = new ExpiredReservationsBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReservationProcessingOptions
            {
                ExpirationCheckInterval = TimeSpan.FromMinutes(1)
            }),
            NullLogger<ExpiredReservationsBackgroundService>.Instance,
            timeProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await useCase.FirstExecution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, useCase.ExecutionCount);

            timeProvider.Advance(TimeSpan.FromMinutes(1));
            await useCase.TwoExecutions.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, useCase.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppingDuringUseCaseExecution_StopsCleanly()
    {
        var useCase = new BlockingExpireUseCase();
        var services = new ServiceCollection();
        services.AddScoped<IExpireRetainedReservations>(_ => useCase);
        await using var serviceProvider = services.BuildServiceProvider();
        var worker = new ExpiredReservationsBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReservationProcessingOptions
            {
                ExpirationCheckInterval = TimeSpan.FromMinutes(1)
            }),
            NullLogger<ExpiredReservationsBackgroundService>.Instance,
            new FakeTimeProvider());

        await worker.StartAsync(CancellationToken.None);
        await useCase.ExecutionStarted.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, useCase.ExecutionCount);
    }

    private sealed class FailingThenSucceedingExpireUseCase : IExpireRetainedReservations
    {
        private readonly TaskCompletionSource firstExecution =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource twoExecutions =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int executionCount;

        public int ExecutionCount => Volatile.Read(ref executionCount);

        public Task FirstExecution => firstExecution.Task;
        public Task TwoExecutions => twoExecutions.Task;

        public Task<ExpireRetainedReservationsResult> ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentExecution = Interlocked.Increment(ref executionCount);
            if (currentExecution == 1)
            {
                firstExecution.TrySetResult();
                throw new InvalidOperationException("Expected test failure.");
            }

            twoExecutions.TrySetResult();
            return Task.FromResult(new ExpireRetainedReservationsResult(
                0,
                [],
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class BlockingExpireUseCase : IExpireRetainedReservations
    {
        private readonly TaskCompletionSource executionStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int executionCount;

        public int ExecutionCount => Volatile.Read(ref executionCount);

        public Task ExecutionStarted => executionStarted.Task;

        public async Task<ExpireRetainedReservationsResult> ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executionCount);
            executionStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ExpireRetainedReservationsResult(0, [], DateTimeOffset.UtcNow);
        }
    }
}
