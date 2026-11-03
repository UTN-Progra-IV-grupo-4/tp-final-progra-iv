using Booking.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Data;

public static class BookingDataServiceCollectionExtensions
{
    public static IServiceCollection AddBookingData(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<BookingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IReservationRepository, ReservationRepository>();

        // Usa el mismo ámbito por petición que BookingDbContext.
        services.AddScoped<ReservationRepository>();

        return services;
    }
}
