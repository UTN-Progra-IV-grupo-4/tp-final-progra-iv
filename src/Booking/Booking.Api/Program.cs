using Booking.Api.Authentication;
using Booking.Api.Logging;
using Booking.Api.Messaging;
using Booking.Api.Reservations;
using Booking.Business;
using Booking.Business.Reservations;
using Microsoft.Extensions.Options;
using ServiceAuthentication;

var builder = WebApplication.CreateBuilder(args);

// Booking sigue una arquitectura N-Layer: Api -> Business -> Data. Cada capa solo conoce a la de
// abajo, por eso Api no accede a Data directamente: pasa siempre por Business, que concentra las
// reglas de reserva. El modelo es anémico a propósito (las entidades solo transportan datos).
// Este archivo es la raíz de composición de la capa Api.
// Como microservicio expone REST, consume solicitudes del broker, cobra contra Payment por gRPC y
// se autentica con client credentials de Keycloak.
var bookingConnectionString = builder.Configuration.GetConnectionString("Booking");
if (string.IsNullOrWhiteSpace(bookingConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Booking' is not configured. Set it with dotnet user-secrets or the ConnectionStrings__Booking environment variable.");
}

builder.Services.AddBookingBusiness(bookingConnectionString);
builder.Services
    .AddOptions<ReservationProcessingOptions>()
    .Bind(builder.Configuration.GetSection(ReservationProcessingOptions.SectionName))
    .Validate(
        options => options.RetentionDuration > TimeSpan.Zero,
        $"{nameof(ReservationProcessingOptions.RetentionDuration)} must be greater than zero.")
    .Validate(
        options => options.ExpirationCheckInterval > TimeSpan.Zero,
        $"{nameof(ReservationProcessingOptions.ExpirationCheckInterval)} must be greater than zero.")
    .Validate(
        options => options.ExpirationBatchSize > 0,
        $"{nameof(ReservationProcessingOptions.ExpirationBatchSize)} must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<ExpiredReservationsBackgroundService>();
builder.Services.AddBookingMessaging(builder.Configuration);
builder.Services.AddServiceAuthentication(builder.Configuration);
// Outgoing: booking-service token (client credentials) for the Payment gRPC client.
builder.Services.AddServiceTokenProvider(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

app.WarnWhenServiceAuthenticationIsNotConfigured();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Before authentication so rejected requests also log with their correlation id.
app.UseCorrelationId();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the entry point to WebApplicationFactory in tests.
public partial class Program;
