namespace Availability.Infrastructure.Messaging;

/// <summary>
/// Settings under <c>Broker:AzureServiceBus</c>. Same shape as the Booking consumer: the
/// publisher is only active when <see cref="Enabled"/> is true, so a missing connection
/// string never breaks local development.
/// </summary>
public sealed class AzureServiceBusPublisherOptions
{
    public const string DefaultTopicName = "reservation-requests";

    public bool Enabled { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = DefaultTopicName;
}
