using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Testcontainers.PostgreSql;

namespace Availability.Tests;

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
        // Los identificadores provienen del modelo de EF, nunca de entradas externas.
        // La tabla de historial de migraciones no forma parte de este modelo.
        var sqlHelper = context.GetService<ISqlGenerationHelper>();
        var tableNames = context.Model.GetRelationalModel().Tables
            .Select(table => sqlHelper.DelimitIdentifier(table.Name, table.Schema));
        var sql = "TRUNCATE TABLE " + string.Join(", ", tableNames) + ";";
        await context.Database.ExecuteSqlRawAsync(sql);
    }
}
