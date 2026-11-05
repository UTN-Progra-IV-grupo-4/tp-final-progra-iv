using Microsoft.Extensions.Options;
using Payment.Grpc.Authentication;
using Payment.Business;
using Payment.Business.Gateways;
using Payment.Grpc.Services;

var builder = WebApplication.CreateBuilder(args);

// Payment sigue una arquitectura N-Layer: Grpc -> Business -> Data. Cada capa solo conoce a la de
// abajo: Grpc expone el contrato y delega en Business, que a su vez usa Data para persistir.
// Es el servicio que cobra para Booking y solo acepta llamadas gRPC autenticadas del cliente de
// servicio de Booking.
// Este archivo es la raíz de composición: es el único lugar que conoce las claves de configuración. Business y Data
// reciben los valores ya resueltos.
var paymentConnectionString = builder.Configuration.GetConnectionString("Payment");
if (string.IsNullOrWhiteSpace(paymentConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Payment' is not configured. Set it with dotnet user-secrets or the ConnectionStrings__Payment environment variable.");
}

builder.Services.AddPaymentBusiness(paymentConnectionString);

// ValidateOnStart: un modo o una demora inválidos frenan el arranque en vez de fallar en el primer cobro.
builder.Services.AddOptions<SimulatedGatewayOptions>()
    .BindConfiguration(SimulatedGatewayOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// El vencimiento tiene que superar la demora de la pasarela: si no, se reclamaría un cobro en curso.
builder.Services.AddOptions<ChargeOptions>()
    .BindConfiguration(ChargeOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate<IOptions<SimulatedGatewayOptions>>(
        (charge, gatewayOptions) => charge.PendingTimeoutSeconds > gatewayOptions.Value.SlowDelaySeconds,
        "Charge:PendingTimeoutSeconds tiene que ser mayor que SimulatedGateway:SlowDelaySeconds.")
    .ValidateOnStart();

builder.Services.AddServiceAuthentication(builder.Configuration);
builder.Services.AddGrpc();

var app = builder.Build();

app.WarnWhenServiceAuthenticationIsNotConfigured();

app.UseAuthentication();
app.UseAuthorization();

// Only booking-service may charge: no token is UNAUTHENTICATED, a token of another client is PERMISSION_DENIED.
app.MapGrpcService<PaymentGrpcService>().RequireAuthorization(ServiceAuthenticationPolicies.BookingService);

app.Run();

// Exposes the entry point to WebApplicationFactory in tests.
public partial class Program;
