using Availability.Application.Events;
using Availability.Infrastructure.Events;
using Availability.Application.Abstractions;
using Availability.Infrastructure.Messaging;
using Availability.Infrastructure.Persistence;
using Availability.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEventQueries(this IServiceCollection services, bool useDemoData)
    {
        services.AddScoped<EventQueryService>();
        if (useDemoData)
        {
            services.AddScoped<IEventCatalog, InMemoryEventCatalog>();
            services.AddScoped<ISeatOccupancyQuery, DemoSeatOccupancyQuery>();
        }
        else
        {
            services.AddScoped<IEventCatalog, EfEventCatalog>();
            services.AddScoped<ISeatOccupancyQuery, UnavailableSeatOccupancyQuery>();
        }
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString, string? brokerProvider = null)
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

        return services;
    }
}
