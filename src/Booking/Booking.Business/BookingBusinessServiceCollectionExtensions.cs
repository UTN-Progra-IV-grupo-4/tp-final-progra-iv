using Booking.Business.Reservations;
using Booking.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booking.Business;

public static class BookingBusinessServiceCollectionExtensions
{
    public static IServiceCollection AddBookingBusiness(this IServiceCollection services, string connectionString)
    {
        services.AddBookingData(connectionString);
        services.AddOptions<ReservationProcessingOptions>();
        services.AddScoped<IReservationRequestProcessor, ReservationRequestProcessor>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
