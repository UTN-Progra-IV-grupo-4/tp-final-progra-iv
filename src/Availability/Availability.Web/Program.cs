using Availability.Application.ReservationRequests;
using Availability.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Web conecta las capas al iniciar; los controladores utilizan Application.
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Availability"),
    builder.Configuration["Broker:Provider"]);
builder.Services.AddScoped<ReservationRequestHandler>();

// Authentication with Keycloak
var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
    ?? "http://localhost:8080/realms/seat-reservation";
var keycloakClientId = builder.Configuration["Keycloak:ClientId"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_FRONTEND_CLIENT_ID")
    ?? "availability-web";

// Cookie scheme as the default so [Authorize] challenges instead of throwing a 500.
// OIDC login plugs the OpenID Connect challenge on top of this cookie scheme.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // The API must answer 401/403 instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = keycloakAuthority;
        options.ClientId = keycloakClientId;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.RequireHttpsMetadata = false;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = "roles"
        };

        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = context =>
            {
                var userId = context.Principal?.FindFirst("sub")?.Value;
                Console.WriteLine($"[AUTH] User authenticated: {userId}");
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
    options.AddPolicy("Public", policy =>
    {
        policy.RequireAssertion(_ => true);
    });

    options.AddPolicy("Authenticated", policy =>
    {
        policy.RequireAuthenticatedUser();
    });
});

// Service to obtain service tokens for inter-service communication
builder.Services.AddScoped<KeycloakTokenService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
