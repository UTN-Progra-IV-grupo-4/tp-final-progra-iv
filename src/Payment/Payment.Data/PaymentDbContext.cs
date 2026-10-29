using Microsoft.EntityFrameworkCore;
using Payment.Data.Entities;

namespace Payment.Data;

// Las opciones (proveedor y connection string) llegan por inyección de dependencias:
// la cadena sale de la configuración o de user-secrets y nunca queda escrita en el código.
public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Charge> Charges => Set<Charge>();

    // Fluent API en vez de atributos: la entidad sigue siendo solo datos y el mapeo queda en esta capa.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Charge>(charge =>
        {
            // Garantía real de idempotencia: aunque dos réplicas reciban el mismo reintento a la vez,
            // la base acepta un solo cobro por reserva. La consulta previa en Business no alcanza sola.
            charge.HasIndex(c => c.ReservationId).IsUnique();

            // Como texto: se lee directo en psql y no se rompe si alguien reordena el enum.
            charge.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);

            charge.Property(c => c.FailureReason).HasMaxLength(200);
        });
    }
}
