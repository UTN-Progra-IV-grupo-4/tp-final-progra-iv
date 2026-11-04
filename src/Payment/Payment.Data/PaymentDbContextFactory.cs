using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Payment.Data;

// Solo la usan las herramientas de EF Core: sin ella, dotnet ef arranca Program.cs, que se niega a
// iniciar sin ConnectionStrings:Payment. Así una migración se crea sin base ni secretos cargados.
public class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        // Mismas fuentes que la app en Development: database update apunta a la misma base que el servicio.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("TpFinal.Payment.Grpc")
            .AddEnvironmentVariables()
            .Build();

        // Sin contraseña a propósito: migrations add no se conecta, solo necesita saber que es Npgsql.
        var connectionString = configuration.GetConnectionString("Payment")
            ?? "Host=localhost;Database=payment_design_time";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PaymentDbContext(options);
    }
}
