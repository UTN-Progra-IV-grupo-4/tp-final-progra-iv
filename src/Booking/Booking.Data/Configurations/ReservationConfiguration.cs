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
            .ValueGeneratedNever();

        builder.Property(reservation => reservation.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(reservation => reservation.SeatId)
            .HasColumnName("seat_id")
            .IsRequired();

        // Garantía real contra la doble reserva: la base impide dos reservas activas del mismo asiento,
        // sin importar cuántas réplicas de Booking compitan. Es un índice único PARCIAL (solo
        // Retained y Confirmed) porque una reserva Rejected o Expired libera el asiento y no debe
        // bloquear reservas futuras.
        builder.HasIndex(reservation => new { reservation.EventId, reservation.SeatId })
            .IsUnique()
            .HasFilter("\"status\" IN ('Retained', 'Confirmed')");

        // Acelera la búsqueda de retenciones vencidas; incluye solo las Retained porque son las únicas
        // que el proceso de expiración necesita encontrar.
        builder.HasIndex(reservation => new { reservation.ExpiresAt, reservation.Id })
            .HasFilter("\"status\" = 'Retained'");

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
