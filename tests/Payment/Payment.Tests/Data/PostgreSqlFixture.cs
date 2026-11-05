using Microsoft.EntityFrameworkCore;
using Payment.Data;
using Testcontainers.PostgreSql;

namespace Payment.Tests.Data;

// PostgreSQL real y no un proveedor en memoria: la idempotencia depende del índice único y de los
// UPDATE condicionales, que solo se comportan como en producción contra el motor de verdad.
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("payment_tests")
        .WithUsername("payment_tests")
        .WithPassword("payment_tests")
        .Build();

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        // Las migraciones y no EnsureCreated: así los tests también prueban que la migración está al día.
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
    }

    public PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;

        return new PaymentDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE charges;");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "Payment PostgreSQL integration";
}
