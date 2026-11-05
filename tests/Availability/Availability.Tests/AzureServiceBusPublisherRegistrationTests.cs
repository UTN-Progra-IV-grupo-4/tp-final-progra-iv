using Availability.Application.Abstractions;
using Availability.Infrastructure;
using Availability.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Tests;

public class AzureServiceBusPublisherRegistrationTests
{
    // Valid format, never contacted: the client connects lazily on the first send.
    private const string FakeConnectionString =
        "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=dGVzdA==";

    private static ServiceProvider BuildProvider(string? brokerProvider, AzureServiceBusPublisherOptions? options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            "Host=localhost;Database=availability;Username=postgres;Password=postgres",
            brokerProvider,
            serviceBusOptions: options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddInfrastructure_WhenAzureServiceBusEnabled_RegistersAzureServiceBusPublisher()
    {
        // The host disposes the container asynchronously: ServiceBusClient is IAsyncDisposable only.
        await using var provider = BuildProvider("AzureServiceBus", new AzureServiceBusPublisherOptions
        {
            Enabled = true,
            ConnectionString = FakeConnectionString,
            TopicName = "reservation-requests"
        });

        Assert.IsType<AzureServiceBusMessagePublisher>(provider.GetRequiredService<IMessagePublisher>());
    }

    [Fact]
    public async Task AddInfrastructure_WhenAzureServiceBusEnabled_SharesOnePublisherAcrossScopes()
    {
        // The host disposes the container asynchronously: ServiceBusClient is IAsyncDisposable only.
        await using var provider = BuildProvider("AzureServiceBus", new AzureServiceBusPublisherOptions
        {
            Enabled = true,
            ConnectionString = FakeConnectionString
        });

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<IMessagePublisher>(),
            secondScope.ServiceProvider.GetRequiredService<IMessagePublisher>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddInfrastructure_WhenAzureServiceBusIsNotEnabled_KeepsTheLoggingPublisher(bool hasOptions)
    {
        // The default appsettings.json has Provider=AzureServiceBus with Enabled=false and no
        // connection string: publishing must keep working (log only) instead of failing.
        var options = hasOptions ? new AzureServiceBusPublisherOptions { Enabled = false } : null;
        using var provider = BuildProvider("AzureServiceBus", options);
        using var scope = provider.CreateScope();

        Assert.IsType<LoggingMessagePublisher>(scope.ServiceProvider.GetRequiredService<IMessagePublisher>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfrastructure_WhenEnabledWithoutConnectionString_FailsAtStartup(string connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildProvider(
            "AzureServiceBus",
            new AzureServiceBusPublisherOptions { Enabled = true, ConnectionString = connectionString }));

        Assert.Contains("ConnectionString", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_WhenEnabledWithoutTopicName_FailsAtStartup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildProvider(
            "AzureServiceBus",
            new AzureServiceBusPublisherOptions
            {
                Enabled = true,
                ConnectionString = FakeConnectionString,
                TopicName = " "
            }));

        Assert.Contains("TopicName", exception.Message);
    }
}
