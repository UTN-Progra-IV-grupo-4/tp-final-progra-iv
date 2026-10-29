using Booking.Business.Services;
using Booking.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Business;

public static class BookingBusinessServiceCollectionExtensions
{
    public static IServiceCollection AddBookingBusiness(this IServiceCollection services, string connectionString)
    {
        services.AddBookingData(connectionString);

        // Comparte el ámbito con el repositorio y el contexto.
        services.AddScoped<ReservationStatusService>();

        return services;
    }
}
