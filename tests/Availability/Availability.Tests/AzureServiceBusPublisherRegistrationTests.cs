using Availability.Application.Abstractions;
using Availability.Infrastructure;
using Availability.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Tests;

public class AzureServiceBusPublisherRegistrationTests
{
    [Fact]
    public void AddInfrastructure_WhenAzureServiceBusProviderConfigured_RegistersAzureServiceBusPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost;Database=availability;Username=postgres;Password=postgres", "AzureServiceBus");

        using var provider = services.BuildServiceProvider();

        var publisher = provider.GetRequiredService<IMessagePublisher>();

        Assert.IsType<AzureServiceBusMessagePublisher>(publisher);
    }
}
