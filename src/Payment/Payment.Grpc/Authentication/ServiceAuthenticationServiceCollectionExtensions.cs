using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Payment.Grpc.Authentication;

internal static class ServiceAuthenticationServiceCollectionExtensions
{
    private const string AuthorizedPartyClaimType = "azp";

    public static IServiceCollection AddServiceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ServiceAuthenticationOptions>()
            .Bind(configuration.GetSection(ServiceAuthenticationOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Audience) &&
                           !string.IsNullOrWhiteSpace(options.AllowedClientId),
                $"{nameof(ServiceAuthenticationOptions.Audience)} and {nameof(ServiceAuthenticationOptions.AllowedClientId)} are required.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Options are resolved lazily so configuration overrides are always honored.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ServiceAuthenticationOptions>>(ConfigureJwtBearer);

        // Unlike Booking (AddControllers), gRPC does not register the authorization services.
        services.AddAuthorization();

        services
            .AddOptions<AuthorizationOptions>()
            .Configure<IOptions<ServiceAuthenticationOptions>>(
                (authorization, serviceOptions) =>
                    authorization.AddPolicy(
                        ServiceAuthenticationPolicies.BookingService,
                        policy => policy
                            .RequireAuthenticatedUser()
                            .RequireClaim(
                                AuthorizedPartyClaimType,
                                serviceOptions.Value.AllowedClientId)));

        return services;
    }

    public static void WarnWhenServiceAuthenticationIsNotConfigured(
        this WebApplication app)
    {
        var options = app.Services
            .GetRequiredService<IOptions<ServiceAuthenticationOptions>>()
            .Value;

        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            app.Logger.LogWarning(
                "{Setting} is not configured: every protected endpoint will answer 401 until it is set.",
                $"{ServiceAuthenticationOptions.SectionName}:{nameof(ServiceAuthenticationOptions.Authority)}");
        }
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions jwtBearerOptions,
        IOptions<ServiceAuthenticationOptions> serviceOptions)
    {
        var options = serviceOptions.Value;

        // Keep claim names as issued (azp, aud) instead of the legacy WS-* mapping.
        jwtBearerOptions.MapInboundClaims = false;
        jwtBearerOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;

        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            // Fail closed: JwtBearer throws on first use without an authority, which would
            // surface as a 500. An empty static configuration plus an explicit failure
            // keeps the app starting and turns every token into a 401.
            jwtBearerOptions.Configuration = new OpenIdConnectConfiguration();
            jwtBearerOptions.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    context.Fail("Service authentication is not configured.");
                    return Task.CompletedTask;
                }
            };
            return;
        }

        var issuer = options.Authority.TrimEnd('/');
        jwtBearerOptions.Authority = issuer;
        jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }
}
