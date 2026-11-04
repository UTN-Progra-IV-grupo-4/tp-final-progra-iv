using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Authentication with Keycloak
var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
    ?? "http://localhost:8080/realms/seat-reservation";
var keycloakAudience = builder.Configuration["Keycloak:Audience"]
    ?? "payment-service";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.Authority = keycloakAuthority;
    options.RequireHttpsMetadata = false;
    options.Audience = keycloakAudience;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = keycloakAuthority,
        ValidAudiences = new[] { keycloakAudience }
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var clientId = context.Principal?.FindFirst("azp")?.Value;
            Console.WriteLine($"[AUTH] Token validated - Client: {clientId}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"[AUTH ERROR] {context.Exception.Message}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServiceOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        // Validate that token comes from booking-service
        policy.RequireClaim("azp", "booking-service");
    });
});

// Add services to the container.
builder.Services.AddGrpc();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
