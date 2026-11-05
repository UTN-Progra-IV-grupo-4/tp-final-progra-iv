using Availability.Application.Abstractions;
using Availability.Application.Events;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Events;
using Availability.Infrastructure.Messaging;
using Availability.Infrastructure.Persistence;
using Availability.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            var baseAddress = bookingOptions.GetBaseAddress();
            var timeout = TimeSpan.FromSeconds(bookingOptions.TimeoutSeconds);
            services.AddScoped<IEventCatalog, EfEventCatalog>();
            services.TryAddScoped<IBookingServiceTokenProvider, PendingBookingServiceTokenProvider>();
            services.AddHttpClient<ISeatOccupancyQuery, BookingSeatOccupancyQuery>(client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = timeout;
            })
            // Un 302 de Booking debe tratarse como respuesta inesperada, no seguirse.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        }
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString, string? brokerProvider = null, bool useFakeBookingClient = false)
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

        services.AddScoped<IMessagePublisher>(provider =>
        {
            if (string.Equals(brokerProvider, "RabbitMq", StringComparison.OrdinalIgnoreCase))
            {
                return new RabbitMqMessagePublisher(provider.GetRequiredService<ILogger<RabbitMqMessagePublisher>>());
            }

            // Temporary placeholder until the real broker implementation is introduced in a dedicated PR.
            return new LoggingMessagePublisher(provider.GetRequiredService<ILogger<LoggingMessagePublisher>>());
        });

        // The fake only runs in Development; elsewhere the status is reported as unavailable
        // until the real HTTP client against Booking (#21) is integrated.
        if (useFakeBookingClient)
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
