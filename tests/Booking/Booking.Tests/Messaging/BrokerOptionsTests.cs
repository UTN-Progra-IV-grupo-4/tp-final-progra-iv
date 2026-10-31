using Booking.Api.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Messaging;

public sealed class BrokerOptionsTests
{
    [Fact]
    public void Options_WhenConsumerIsDisabled_DoNotRequireBrokerCredentials()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["Broker:Provider"] = BrokerOptions.AzureServiceBusProvider,
            ["Broker:AzureServiceBus:Enabled"] = "false"
        });

        Assert.False(options.AzureServiceBus.Enabled);
    }

    [Fact]
    public void Options_WhenConsumerIsEnabledWithoutBrokerConfiguration_FailValidation()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(new Dictionary<string, string?>
            {
                ["Broker:Provider"] = BrokerOptions.AzureServiceBusProvider,
                ["Broker:AzureServiceBus:Enabled"] = "true"
            }));

        Assert.Contains("connection string", exception.Message);
    }

    private static BrokerOptions ResolveOptions(
        IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBookingMessaging(configuration);
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<BrokerOptions>>().Value;
    }
}
