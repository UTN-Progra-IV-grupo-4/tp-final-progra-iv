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
// Web conecta las implementaciones del catálogo y del envío de solicitudes.
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Availability"),
    builder.Configuration["Broker:Provider"],
    useFakeBookingClient: builder.Environment.IsDevelopment(),
    serviceBusOptions: builder.Configuration.GetSection("Broker:AzureServiceBus").Get<AzureServiceBusPublisherOptions>(),
    // Demo mode never calls Booking, so the real status client is not offered there.
    bookingOptions: useDemoData ? null : bookingOptions);
builder.Services.AddScoped<ReservationRequestHandler>();
builder.Services.AddEventQueries(useDemoData, bookingOptions);
builder.Services.AddSingleton(new EventDisplayOptions(useDemoData));
builder.Services.AddScoped<ReservationStatusHandler>();

// Authentication configuration
var useDevelopmentAuth = builder.Configuration.GetValue<bool>("DevelopmentAuthentication:Enabled", false);
if (useDevelopmentAuth && !builder.Environment.IsDevelopment())
{
    // A fixed-user login must never exist outside Development, even if the flag leaks into
    // appsettings.json or an environment variable: refuse to start instead of ignoring it.
    throw new InvalidOperationException("DevelopmentAuthentication:Enabled solo está permitido en Development.");
}

// Cookie scheme as the default so [Authorize] challenges instead of throwing a 500.
// OpenID Connect is only invoked explicitly by AccountController for the MVC login.
var authenticationBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";

        // The API must answer 401/403 instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

if (useDevelopmentAuth)
{
    // Development mode: fixed user, only registered when explicitly enabled in Development.
    authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationHandler.SchemeName, options => { });
}
else
{
    // Production mode: OpenID Connect with Keycloak (public client, authorization code + PKCE).
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
        // The id_token is needed as id_token_hint so that logout ends the Keycloak session.
        options.SaveTokens = true;
        // Plain HTTP metadata is only acceptable against the local Keycloak of the dev compose;
        // outside Development it needs an explicit Keycloak:RequireHttpsMetadata=false.
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Keycloak:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
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
            OnAuthenticationFailed = context =>
            {
                // Only the exception type: messages can echo parts of the response.
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Availability.Web.Authentication")
                    .LogWarning("OpenID Connect authentication failed: {ExceptionType}", context.Exception.GetType().Name);
                return Task.CompletedTask;
            }
        };
    });
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
