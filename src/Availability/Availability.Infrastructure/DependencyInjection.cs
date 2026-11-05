using Availability.Application.Abstractions;
using Availability.Application.Events;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Events;
using Availability.Infrastructure.Messaging;
using Availability.Infrastructure.Persistence;
using Availability.Infrastructure.Persistence.Repositories;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEventQueries(this IServiceCollection services, bool useDemoData,
        BookingQueryOptions? bookingOptions = null)
    {
        services.AddScoped<EventQueryService>();
        if (useDemoData)
        {
            services.AddScoped<IEventCatalog, InMemoryEventCatalog>();
            services.AddScoped<ISeatOccupancyQuery, DemoSeatOccupancyQuery>();
        }
        else
        {
            bookingOptions ??= new BookingQueryOptions();
            services.AddScoped<IEventCatalog, EfEventCatalog>();
            // Shared with the status client: base address, timeout, redirects and service token.
            // The token provider itself (ServiceAuthentication) is registered by the host as a singleton.
            services.AddBookingHttpClient<ISeatOccupancyQuery, BookingSeatOccupancyQuery>(bookingOptions);
        }
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString, string? brokerProvider = null, bool useFakeBookingClient = false,
        AzureServiceBusPublisherOptions? serviceBusOptions = null, BookingQueryOptions? bookingOptions = null)
    {
        services.AddDbContext<AvailabilityDbContext>(options =>
        {
            // Se evalúa al solicitar el contexto: la página MVC inicial puede abrirse
            // sin Postgres, pero una operación de datos exige configuración explícita.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Falta ConnectionStrings:Availability. Configurala con user-secrets o " +
                    "ConnectionStrings__Availability usando los valores de tu .env local del compose.");
            }

            // La configuración de ejecución y la fábrica de migraciones deben coincidir.
            options.UseNpgsql(connectionString, postgres => postgres.SetPostgresVersion(16, 0));
        });

        services.AddScoped<IReservationRequestRepository, ReservationRequestRepository>();

        var useAzureServiceBus = string.Equals(brokerProvider, "AzureServiceBus", StringComparison.OrdinalIgnoreCase)
            && serviceBusOptions is { Enabled: true };
        if (useAzureServiceBus)
        {
            // Fail at startup instead of on the first reservation request.
            if (string.IsNullOrWhiteSpace(serviceBusOptions!.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Broker:AzureServiceBus:ConnectionString is required when Broker:AzureServiceBus:Enabled is true.");
            }

            if (string.IsNullOrWhiteSpace(serviceBusOptions.TopicName))
            {
                throw new InvalidOperationException(
                    "Broker:AzureServiceBus:TopicName is required when Broker:AzureServiceBus:Enabled is true.");
            }

            // One client (one AMQP connection) for the whole process; the container disposes it.
            services.AddSingleton(_ => new ServiceBusClient(serviceBusOptions.ConnectionString));
            services.AddSingleton<IMessagePublisher>(provider => new AzureServiceBusMessagePublisher(
                provider.GetRequiredService<ServiceBusClient>(),
                serviceBusOptions.TopicName,
                provider.GetRequiredService<ILogger<AzureServiceBusMessagePublisher>>()));
        }
        else
        {
            services.AddScoped<IMessagePublisher>(provider =>
            {
                if (string.Equals(brokerProvider, "RabbitMq", StringComparison.OrdinalIgnoreCase))
                {
                    return new RabbitMqMessagePublisher(provider.GetRequiredService<ILogger<RabbitMqMessagePublisher>>());
                }

                // Without Broker:AzureServiceBus:Enabled the request is only logged, as before.
                return new LoggingMessagePublisher(provider.GetRequiredService<ILogger<LoggingMessagePublisher>>());
            });
        }

        // Booking status client (#16). The real HTTP client is used whenever Booking:BaseUrl is
        // configured (bookingOptions is null in Demo mode, which serves demo data and never calls Booking).
        // Without it, the fake only runs in Development; anywhere else the status is reported as
        // unavailable instead of inventing one.
        if (bookingOptions is not null && !string.IsNullOrWhiteSpace(bookingOptions.BaseUrl))
        {
            services.AddBookingHttpClient<IBookingStatusClient, BookingStatusClient>(bookingOptions);
        }
        else if (useFakeBookingClient)
        {
            services.AddScoped<IBookingStatusClient, FakeBookingStatusClient>();
        }
        else
        {
            services.AddScoped<IBookingStatusClient, UnavailableBookingStatusClient>();
        }

        return services;
    }
}
