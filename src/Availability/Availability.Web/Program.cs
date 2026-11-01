using Availability.Web.Abstractions;
using Availability.Web.Application;
using Availability.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IReservationRequestRepository, InMemoryReservationRequestRepository>();

var brokerProvider = builder.Configuration["Broker:Provider"] ?? "ServiceBus";
builder.Services.AddScoped<IMessagePublisher>(provider =>
{
    if (string.Equals(brokerProvider, "RabbitMq", StringComparison.OrdinalIgnoreCase))
    {
        return new RabbitMqMessagePublisher(provider.GetRequiredService<ILogger<RabbitMqMessagePublisher>>());
    }

    // Temporary placeholder until the real broker implementation is introduced in a dedicated PR.
    return new LoggingMessagePublisher(provider.GetRequiredService<ILogger<LoggingMessagePublisher>>());
});

builder.Services.AddScoped<ReservationRequestHandler>();

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

app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
