using Booking.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Business;

public static class BookingBusinessServiceCollectionExtensions
{
    public static IServiceCollection AddBookingBusiness(this IServiceCollection services, string connectionString)
    {
        services.AddBookingData(connectionString);

        return services;
    }
}
