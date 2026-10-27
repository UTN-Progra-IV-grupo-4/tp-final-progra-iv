using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Availability.Infrastructure.Persistence.Configurations;

public class ReservationRequestConfiguration : IEntityTypeConfiguration<ReservationRequest>
{
    public void Configure(EntityTypeBuilder<ReservationRequest> builder)
    {
        builder.ToTable("ReservationRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.UserId).IsRequired().HasMaxLength(200);
        builder.Property(r => r.RequestedAt).IsRequired();
        // Admitimos varias solicitudes para un asiento; Booking resuelve la concurrencia.
        // Restrict evita borrar un asiento que forma parte del historial de solicitudes.
        builder.HasOne(r => r.Seat).WithMany()
            .HasForeignKey(r => r.SeatId).OnDelete(DeleteBehavior.Restrict);
    }
}
