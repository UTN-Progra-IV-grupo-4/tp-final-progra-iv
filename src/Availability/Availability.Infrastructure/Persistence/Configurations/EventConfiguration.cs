using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Availability.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        // Mapeo explícito como en Booking: evita dependencias adicionales de convenciones.
        builder.ToTable("events");
        builder.HasKey(e => e.Id);
        // Los identificadores se generan en la aplicación y pueden viajar entre servicios.
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
        // PostgreSQL almacena instantes UTC; los casos de uso deberán enviar offset cero.
        builder.Property(e => e.StartsAt).HasColumnName("starts_at").IsRequired();
    }
}
