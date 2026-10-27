using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Availability.Infrastructure.Persistence.Configurations;

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Number).IsRequired().HasMaxLength(50);
        // Un número se puede repetir en eventos distintos, pero no en el mismo evento.
        builder.HasIndex(s => new { s.EventId, s.Number }).IsUnique();
        builder.HasOne(s => s.Event).WithMany(e => e.Seats)
            .HasForeignKey(s => s.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
