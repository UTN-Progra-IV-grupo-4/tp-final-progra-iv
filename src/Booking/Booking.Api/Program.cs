using Booking.Api.Messaging;
using Booking.Business;
using Booking.Business.Reservations;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var bookingConnectionString = builder.Configuration.GetConnectionString("Booking");
if (string.IsNullOrWhiteSpace(bookingConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Booking' is not configured. Set it with dotnet user-secrets or the ConnectionStrings__Booking environment variable.");
}

builder.Services.AddBookingBusiness(bookingConnectionString);
builder.Services.Configure<ReservationProcessingOptions>(
    builder.Configuration.GetSection("ReservationProcessing"));
builder.Services.AddBookingMessaging(builder.Configuration);

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
