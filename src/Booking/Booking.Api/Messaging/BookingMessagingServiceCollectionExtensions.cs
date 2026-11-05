using Booking.Business.Payments;
using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;
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

        var retentionDuration = configuration
            .GetSection(ReservationProcessingOptions.SectionName)
            .GetValue<TimeSpan>(nameof(ReservationProcessingOptions.RetentionDuration));

        services
            .AddOptions<PaymentProcessingOptions>()
            .Bind(configuration.GetSection("Payment"))
            .Validate(
                options => options.IsValidFor(retentionDuration),
                "When Payment:Enabled is true, amount, currency, attempts, deadline and retry delay must be valid, and the maximum call time must fit within half the retention duration.")
            .Validate<IServiceProviderIsService>(
                (options, serviceProviderIsService) =>
                    !options.Enabled ||
                    serviceProviderIsService.IsService(typeof(IPaymentChargeClient)),
                "When Payment:Enabled is true, an IPaymentChargeClient must be registered. The gRPC adapter is still pending, so keep Payment:Enabled false until it exists.")
            .ValidateOnStart();
        services.Configure<PaymentConsumerOptions>(configuration.GetSection("Payment"));

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
