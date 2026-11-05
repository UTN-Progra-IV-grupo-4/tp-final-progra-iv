using Booking.Business.Payments;
using Booking.Business.Reservations;
using Booking.Api.Payments;
using Contracts.Payment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceAuthentication;

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
                "When Payment:Enabled is true, an IPaymentChargeClient must be registered.")
            .ValidateOnStart();
        services.Configure<PaymentConsumerOptions>(configuration.GetSection("Payment"));

        services
            .AddOptions<PaymentGrpcOptions>()
            .Bind(configuration.GetSection("Payment"))
            .Validate(
                options => !configuration.GetValue<bool>("Payment:Enabled") || options.HasValidAddress(),
                "Payment:Address must be an absolute HTTP or HTTPS URL when payment is enabled.")
            .ValidateOnStart();

        if (configuration.GetValue<bool>("Payment:Enabled"))
        {
            // The generated client and its adapter are registered together. An enabled consumer
            // cannot start processing messages without an IPaymentChargeClient.
            // Payment only accepts the booking-service token, so the client always carries it.
            services.AddServiceTokenProvider(configuration);
            services.AddGrpcClient<PaymentService.PaymentServiceClient>(options =>
                    options.Address = new Uri(configuration["Payment:Address"]!))
                .AddServiceBearerToken();
            services.AddScoped<IPaymentChargeClient, GrpcPaymentChargeClient>();
        }
        else
        {
            // Without this, Development fails at startup: the scoped ReservationPaymentProcessor
            // is always registered and needs an IPaymentChargeClient to be constructible.
            services.AddScoped<IPaymentChargeClient, DisabledPaymentChargeClient>();
        }

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
