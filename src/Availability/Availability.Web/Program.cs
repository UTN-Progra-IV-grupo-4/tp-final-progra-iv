using Availability.Application.ReservationRequests;
using Availability.Infrastructure;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Messaging;
using Availability.Web.Authentication;
using Availability.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ServiceAuthentication;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
// Web conecta las implementaciones del catálogo y del envío de solicitudes.
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Availability"),
    builder.Configuration["Broker:Provider"],
    useFakeBookingClient: builder.Environment.IsDevelopment(),
    serviceBusOptions: builder.Configuration.GetSection("Broker:AzureServiceBus").Get<AzureServiceBusPublisherOptions>());
builder.Services.AddScoped<ReservationRequestHandler>();
var querySource = builder.Configuration["EventQueries:Source"] ?? "Database";
if (querySource != "Demo" && querySource != "Database")
{
    throw new InvalidOperationException("EventQueries:Source debe ser Demo o Database.");
}
var useDemoData = querySource == "Demo";
if (useDemoData && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("El modo Demo solo está permitido en Development.");
}
var bookingOptions = builder.Configuration.GetSection("Booking").Get<BookingQueryOptions>() ?? new BookingQueryOptions();
builder.Services.AddEventQueries(useDemoData, bookingOptions);
builder.Services.AddSingleton(new EventDisplayOptions(useDemoData));
builder.Services.AddScoped<ReservationStatusHandler>();

// Authentication configuration
var useDevelopmentAuth = builder.Configuration.GetValue<bool>("DevelopmentAuthentication:Enabled", false)
    && builder.Environment.IsDevelopment();

// Cookie scheme as the default so [Authorize] challenges instead of throwing a 500.
var authenticationBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // The API must answer 401/403 instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            // For API endpoints, return 401. For MVC pages, redirect to login.
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(context.Request.Path));
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect("/Account/AccessDenied");
            return Task.CompletedTask;
        };
    });

if (useDevelopmentAuth)
{
    // Development mode: use fixed user authentication
    authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>("Development", options => { });
    Console.WriteLine("[AUTH] Using DEVELOPMENT authentication (fixed user)");
}
else
{
    // Production mode: use OpenID Connect with Keycloak
    var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
        ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
        ?? "http://localhost:8080/realms/seat-reservation";
    var keycloakClientId = builder.Configuration["Keycloak:ClientId"]
        ?? Environment.GetEnvironmentVariable("KEYCLOAK_FRONTEND_CLIENT_ID")
        ?? "availability-web";

    authenticationBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
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

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";

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

    Console.WriteLine($"[AUTH] Using OPENID CONNECT with Keycloak: {keycloakAuthority}");
}

// Outgoing: availability-service token (client credentials) for the calls to Booking.
// Independent from the cookie scheme above, which stays the default for end users.
builder.Services.AddServiceTokenProvider(builder.Configuration);

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
