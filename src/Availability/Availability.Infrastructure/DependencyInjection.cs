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
                    "ConnectionStrings__Availability usando los valores de tu .env local del compose.");
            }

            // La configuración de ejecución y la fábrica de migraciones deben coincidir.
            options.UseNpgsql(connectionString, postgres => postgres.SetPostgresVersion(16, 0));
        });
        return services;
    }
}
