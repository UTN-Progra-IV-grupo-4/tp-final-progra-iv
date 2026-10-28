using Booking.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Data.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(reservation => reservation.SeatId)
            .HasColumnName("seat_id")
            .IsRequired();

        builder.Property(reservation => reservation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reservation => reservation.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(reservation => reservation.ExpiresAt)
            .HasColumnName("expires_at");
    }
}
