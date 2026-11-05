using Booking.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Data;

// Capa Data del N-Layer de Booking (Api -> Business -> Data): persistencia con EF Core sobre
// PostgreSQL. Es la capa más baja y no conoce a Business ni a Api.
public static class BookingDataServiceCollectionExtensions
{
    public static IServiceCollection AddBookingData(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<BookingDbContext>(options => options.UseNpgsql(connectionString));

        // Usa el mismo ámbito por petición que BookingDbContext.
        services.AddScoped<IReservationRepository, ReservationRepository>();
        return services;
    }
}
