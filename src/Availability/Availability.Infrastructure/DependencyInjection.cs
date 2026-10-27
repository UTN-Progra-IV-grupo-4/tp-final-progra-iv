using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<AvailabilityDbContext>(options =>
        {
            // Se evalúa al solicitar el contexto: la página MVC inicial puede abrirse
            // sin Postgres, pero una operación de datos exige configuración explícita.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Falta ConnectionStrings:Availability. Configurala con user-secrets o " +
                    "ConnectionStrings__Availability. El contenedor y el usuario dependen del issue #3.");
            }

            options.UseNpgsql(connectionString);
        });
        return services;
    }
}
