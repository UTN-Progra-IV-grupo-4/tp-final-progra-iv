using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Data;

public static class BookingDataServiceCollectionExtensions
{
    public static IServiceCollection AddBookingData(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<BookingDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
