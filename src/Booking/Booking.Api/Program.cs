using Booking.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var bookingConnectionString = builder.Configuration.GetConnectionString("Booking");
if (string.IsNullOrWhiteSpace(bookingConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Booking' is not configured. Set it with dotnet user-secrets or the ConnectionStrings__Booking environment variable.");
}

builder.Services.AddBookingData(bookingConnectionString);

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

app.UseAuthorization();

app.MapControllers();

app.Run();
