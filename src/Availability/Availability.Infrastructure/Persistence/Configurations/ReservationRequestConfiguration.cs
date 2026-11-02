using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Availability.Infrastructure.Persistence.Configurations;

public class ReservationRequestConfiguration : IEntityTypeConfiguration<ReservationRequest>
{
    public void Configure(EntityTypeBuilder<ReservationRequest> builder)
    {
        builder.ToTable("reservation_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.SeatId).HasColumnName("seat_id");
        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(200);
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at").IsRequired();
        // Admitimos varias solicitudes para un asiento; Booking resuelve la concurrencia.
        // Restrict evita borrar un asiento que forma parte del historial de solicitudes.
        builder.HasOne(r => r.Seat).WithMany()
            .HasForeignKey(r => r.SeatId).OnDelete(DeleteBehavior.Restrict);
    }
}
