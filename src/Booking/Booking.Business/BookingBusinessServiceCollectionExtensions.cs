using Booking.Business.Payments;
using Booking.Business.Services;
using Booking.Business.Reservations;
using Booking.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booking.Business;

// Capa Business del N-Layer de Booking (Api -> Business -> Data): concentra las reglas de reserva y
// de cobro. Solo conoce a Data; los detalles de transporte (HTTP, broker, gRPC) quedan en Api, que
// implementa los puertos que Business define. Registra también Data para que Api no la referencie.
public static class BookingBusinessServiceCollectionExtensions
{
    public static IServiceCollection AddBookingBusiness(this IServiceCollection services, string connectionString)
    {
        services.AddBookingData(connectionString);
        services.AddOptions<ReservationProcessingOptions>();
        services.AddOptions<PaymentProcessingOptions>();
        services.AddScoped<IReservationRequestProcessor, ReservationRequestProcessor>();
        services.AddScoped<ReservationPaymentProcessor>();
        services.AddScoped<IExpireRetainedReservations, ExpireRetainedReservations>();
        services.TryAddSingleton(TimeProvider.System);

        // Comparte el ámbito con el repositorio y el contexto.
        services.AddScoped<ReservationStatusService>();
        services.AddScoped<OccupiedSeatsService>();

        return services;
    }
}
