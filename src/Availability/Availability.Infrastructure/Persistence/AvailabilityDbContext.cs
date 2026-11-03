using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Persistence;

// EF Core queda en Infrastructure para no acoplar Domain al motor de persistencia.
public class AvailabilityDbContext(DbContextOptions<AvailabilityDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<ReservationRequest> ReservationRequests => Set<ReservationRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AvailabilityDbContext).Assembly);
    }
}
