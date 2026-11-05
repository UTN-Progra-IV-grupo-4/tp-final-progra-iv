using Availability.Application.ReservationRequests;
using Availability.Infrastructure;
using Availability.Infrastructure.Booking;
using Availability.Infrastructure.Messaging;
using Availability.Web.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

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

// Cookie scheme as the default so [Authorize] challenges instead of throwing a 500.
// #14 (OIDC login) will plug the OpenID Connect challenge on top of this cookie scheme.
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
    });

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
