using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

internal static class BookingMessagingServiceCollectionExtensions
{
    public static IServiceCollection AddBookingMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<BrokerOptions>()
            .Bind(configuration.GetSection(BrokerOptions.SectionName))
            .Validate(
                options => IsValid(options),
                "When Broker:AzureServiceBus:Enabled is true, Broker:Provider must be AzureServiceBus and the connection string, topic and subscription are required.")
            .ValidateOnStart();

        services.AddSingleton<ReservationRequestMapper>();
        services.AddSingleton<IReservationRequestMessageHandler, ReservationRequestMessageHandler>();
        services.AddSingleton<ReservationRequestMessageProcessor>();
        services.AddSingleton<IReservationRequestConsumer, AzureServiceBusReservationRequestConsumer>();
        services.AddHostedService<ReservationRequestConsumerHostedService>();

        return services;
    }

    private static bool IsValid(BrokerOptions options)
    {
        if (!options.AzureServiceBus.Enabled)
        {
            return true;
        }

        return string.Equals(
                   options.Provider,
                   BrokerOptions.AzureServiceBusProvider,
                   StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(options.AzureServiceBus.ConnectionString) &&
               !string.IsNullOrWhiteSpace(options.AzureServiceBus.TopicName) &&
               !string.IsNullOrWhiteSpace(options.AzureServiceBus.SubscriptionName);
    }
}
