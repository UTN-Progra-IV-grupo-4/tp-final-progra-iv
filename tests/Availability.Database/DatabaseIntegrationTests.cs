using Availability.Domain.Entities;
using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Availability.Database.Tests;

// xUnit ejecuta secuencialmente los tests de esta clase porque comparten la fixture.
public sealed class DatabaseIntegrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Migrations_OnEmptyDatabase_CreateCurrentSchema()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Events.ToListAsync());
        Assert.Empty(await context.Seats.ToListAsync());
        Assert.Empty(await context.ReservationRequests.ToListAsync());
    }

    [Fact]
    public async Task PostgreSqlConstraints_RejectInvalidData_AndAllowCompetingRequests()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        // Se reutilizan las mismas comprobaciones manuales: se omiten únicamente
        // los metacomandos de psql, que no son SQL válido para el driver de .NET.
        var lines = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "constraints.sql"));
        var sql = string.Join(Environment.NewLine, lines.Where(line => !line.TrimStart().StartsWith('\\')));
        await context.Database.ExecuteSqlRawAsync(sql);

        Assert.Empty(await context.Events.ToListAsync());
        Assert.Empty(await context.Seats.ToListAsync());
        Assert.Empty(await context.ReservationRequests.ToListAsync());
    }

    [Fact]
    public async Task DemoData_CanBeLoadedTwice_WithoutDuplicatingRows()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "demo-data.sql"));

        await context.Database.ExecuteSqlRawAsync(sql);
        await context.Database.ExecuteSqlRawAsync(sql);

        Assert.Equal(2, await context.Events.CountAsync());
        Assert.Equal(6, await context.Seats.CountAsync());
        Assert.Empty(await context.ReservationRequests.ToListAsync());
    }

    [Fact]
    public async Task Request_WithUtcTimestamp_RoundTripsThroughEfCore()
    {
        await fixture.ResetAsync();
        var instant = new DateTimeOffset(2030, 6, 15, 18, 0, 0, TimeSpan.Zero);
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            Number = "A1",
            Event = new Event { Id = Guid.NewGuid(), Name = "Prueba UTC", StartsAt = instant }
        };
        var request = new ReservationRequest
        {
            Id = Guid.NewGuid(), Seat = seat, UserId = "usuario-prueba", RequestedAt = instant
        };

        await using (var context = fixture.CreateDbContext())
        {
            context.ReservationRequests.Add(request);
            await context.SaveChangesAsync();
        }

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.ReservationRequests.AsNoTracking()
            .Include(r => r.Seat).ThenInclude(s => s.Event).SingleAsync();
        Assert.Equal(request.Id, stored.Id);
        Assert.Equal(seat.Event.Id, stored.Seat.EventId);
        Assert.Equal(instant, stored.RequestedAt);
        Assert.Equal(TimeSpan.Zero, stored.RequestedAt.Offset);
        Assert.Equal(instant, stored.Seat.Event.StartsAt);
    }

    [Fact]
    public async Task SnakeCaseMigration_PreservesExistingRows()
    {
        // Otra base descartable permite simular el esquema ya aplicado antes de la revisión.
        await using var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("availability_upgrade_tests")
            .WithUsername("availability_tests")
            .WithPassword("availability_tests")
            .Build();
        await container.StartAsync();
        var options = new DbContextOptionsBuilder<AvailabilityDbContext>()
            .UseNpgsql(container.GetConnectionString(), postgres => postgres.SetPostgresVersion(16, 0))
            .Options;

        await using var context = new AvailabilityDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync("20261027200657_InitialAvailability");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Events" ("Id", "Name", "StartsAt") VALUES
            ('10000000-0000-0000-0000-000000000001', 'Evento existente', '2030-06-15 18:00:00+00');
            INSERT INTO "Seats" ("Id", "EventId", "Number") VALUES
            ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'A1');
            INSERT INTO "ReservationRequests" ("Id", "SeatId", "UserId", "RequestedAt") VALUES
            ('30000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001',
             'usuario-existente', '2030-06-15 18:00:00+00');
            """);

        await context.Database.MigrateAsync();

        var stored = await context.ReservationRequests.AsNoTracking()
            .Include(r => r.Seat).ThenInclude(s => s.Event).SingleAsync();
        Assert.Equal("usuario-existente", stored.UserId);
        Assert.Equal("Evento existente", stored.Seat.Event.Name);
        Assert.Equal("A1", stored.Seat.Number);
        Assert.Equal(1, await context.Events.CountAsync());
        Assert.Equal(1, await context.Seats.CountAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
