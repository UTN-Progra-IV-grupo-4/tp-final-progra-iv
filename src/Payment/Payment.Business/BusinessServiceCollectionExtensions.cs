using Microsoft.Extensions.DependencyInjection;
using Payment.Business.Gateways;
using Payment.Data;

namespace Payment.Business;

// Cada capa registra sus propias clases: Grpc solo llama a este método y nunca conoce a Data.
public static class BusinessServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentBusiness(this IServiceCollection services, string connectionString)
    {
        services.AddPaymentData(connectionString);

        // Solo el registro: el vínculo con la sección SimulatedGateway y la validación al arrancar
        // los hace Program.cs, así Business no depende de IConfiguration.
        services.AddOptions<SimulatedGatewayOptions>();

        // Singleton: no guarda estado entre cobros y en modo Slow sigue trabajando aunque termine
        // la llamada gRPC que lo pidió, así que su vida no puede depender del scope del pedido.
        services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

        // Scoped como el repositorio que usa: un Singleton retendría el DbContext del primer pedido.
        services.AddScoped<ChargeService>();

        return services;
    }
}
