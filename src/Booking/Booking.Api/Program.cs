using Booking.Api.Messaging;
using Booking.Api.Reservations;
using Booking.Business;
using Booking.Business.Reservations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

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

// Authentication with Keycloak
var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
    ?? "http://localhost:8080/realms/seat-reservation";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.Authority = keycloakAuthority;
    options.RequireHttpsMetadata = false;
    options.Audience = "booking-service";

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = keycloakAuthority,
        ValidAudiences = new[] { "booking-service" }
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var userId = context.Principal?.FindFirst("sub")?.Value;
            var clientId = context.Principal?.FindFirst("azp")?.Value;
            Console.WriteLine($"[AUTH] Token validated - User: {userId}, Client: {clientId}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"[AUTH ERROR] {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Console.WriteLine($"[AUTH] Challenge - 401 Unauthorized");
            return Task.CompletedTask;
        },
        OnForbidden = context =>
        {
            Console.WriteLine($"[AUTH] Forbidden - 403");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServiceOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        // Service accounts include 'azp' claim with client_id
    });
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
