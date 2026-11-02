using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Availability.Database.Tests;

// Base descartable con puerto aleatorio: no usa el compose, el .env ni datos locales.
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("availability_tests")
        .WithUsername("availability_tests")
        .WithPassword("availability_tests") // Credencial exclusiva del contenedor descartable.
        .Build();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var context = CreateDbContext();
        // Usamos las migraciones reales, no EnsureCreated: también verificamos su SQL.
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public AvailabilityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AvailabilityDbContext>()
            .UseNpgsql(container.GetConnectionString(), postgres => postgres.SetPostgresVersion(16, 0))
            .Options;
        return new AvailabilityDbContext(options);
    }

    public async Task ResetAsync()
    {
        // Solo se ejecuta dentro del contenedor de tests, aislado de las bases del equipo.
        await using var context = CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE reservation_requests, seats, events;");
    }
}
