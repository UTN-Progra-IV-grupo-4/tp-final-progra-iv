using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Availability.Infrastructure.Persistence;

// Las herramientas pueden generar migraciones sin iniciar MVC ni conectarse a Postgres.
public class AvailabilityDbContextFactory : IDesignTimeDbContextFactory<AvailabilityDbContext>
{
    public AvailabilityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AvailabilityDbContext>();
        options.UseNpgsql();

        // Sin conexión se permite generar migraciones y SQL. Para aplicarlas, se requiere
        // esta variable local; no hay credenciales ni un servidor ficticio como fallback.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Availability");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseNpgsql(connectionString);
        }

        return new AvailabilityDbContext(options.Options);
    }
}
