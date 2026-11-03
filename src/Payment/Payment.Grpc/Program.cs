using Payment.Business;
using Payment.Business.Gateways;
using Payment.Grpc.Services;

var builder = WebApplication.CreateBuilder(args);

// Raíz de composición: es el único lugar que conoce las claves de configuración. Business y Data
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

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<PaymentGrpcService>();

app.Run();
