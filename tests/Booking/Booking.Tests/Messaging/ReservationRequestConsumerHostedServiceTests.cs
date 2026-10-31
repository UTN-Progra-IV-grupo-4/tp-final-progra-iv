using Booking.Api.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Messaging;

public sealed class ReservationRequestConsumerHostedServiceTests
{
    [Fact]
    public async Task HostedService_DelegatesStartAndStopToConsumer()
    {
        var consumer = new RecordingReservationRequestConsumer();
        var hostedService = new ReservationRequestConsumerHostedService(consumer);

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Equal(1, consumer.StartCount);
        Assert.Equal(1, consumer.StopCount);
    }

    [Fact]
    public async Task AzureConsumer_WhenDisabled_StartsAndStopsWithoutOpeningBrokerConnection()
    {
        var services = new ServiceCollection();
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new ReservationRequestMessageHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new ReservationRequestMapper());
        var consumer = new AzureServiceBusReservationRequestConsumer(
            Options.Create(new BrokerOptions
            {
                Provider = BrokerOptions.AzureServiceBusProvider,
                AzureServiceBus = new AzureServiceBusOptions
                {
                    Enabled = false
                }
            }),
            handler,
            NullLogger<AzureServiceBusReservationRequestConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await consumer.StopAsync(CancellationToken.None);
        await consumer.DisposeAsync();
    }

    private sealed class RecordingReservationRequestConsumer : IReservationRequestConsumer
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }
}
