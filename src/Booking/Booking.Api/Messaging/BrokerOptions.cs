namespace Booking.Api.Messaging;

internal sealed class BrokerOptions
{
    public const string SectionName = "Broker";
    public const string AzureServiceBusProvider = "AzureServiceBus";

    public string Provider { get; set; } = string.Empty;

    public AzureServiceBusOptions AzureServiceBus { get; set; } = new();
}

internal sealed class AzureServiceBusOptions
{
    public bool Enabled { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;

    public string SubscriptionName { get; set; } = string.Empty;
}
