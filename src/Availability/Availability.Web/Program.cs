using Availability.Web.Abstractions;
using Availability.Web.Application;
using Availability.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddControllers();
builder.Services.AddScoped<IReservationRequestRepository, InMemoryReservationRequestRepository>();

var useRabbitMq = builder.Configuration.GetValue<bool>("Messaging:UseRabbitMq");
builder.Services.AddScoped<IMessagePublisher>(provider =>
{
    if (useRabbitMq)
    {
        return new RabbitMqMessagePublisher(provider.GetRequiredService<ILogger<RabbitMqMessagePublisher>>());
    }

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
