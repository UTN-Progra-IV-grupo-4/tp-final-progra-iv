using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payment.Data.Repositories;

namespace Payment.Data;

// Cada capa registra sus propias clases: Business llama a este método y Grpc solo conoce a Business,
// así la presentación nunca referencia a la capa de datos.
public static class DataServiceCollectionExtensions
{
    // Recibe la cadena ya resuelta: la clave en appsettings o user-secrets la conoce el arranque, no esta capa.
    public static IServiceCollection AddPaymentData(this IServiceCollection services, string connectionString)
    {
        // AddDbContext lo registra Scoped: un DbContext por llamada gRPC, porque no es thread-safe.
        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(connectionString));

        // Scoped como el DbContext que usa: si fuera Singleton, retendría el del primer pedido.
        services.AddScoped<IChargeRepository, ChargeRepository>();

        return services;
    }
}
