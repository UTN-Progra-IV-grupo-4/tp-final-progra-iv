using Microsoft.Extensions.DependencyInjection;
using ServiceAuthentication;

namespace Availability.Infrastructure.Booking;

// Single place that wires every HTTP client of Availability towards Booking (seat occupancy, #13,
// and reservation status, #16): same base address, timeout, redirect policy and service token.
internal static class BookingHttpClientRegistration
{
    public static IHttpClientBuilder AddBookingHttpClient<TService, TImplementation>(
        this IServiceCollection services,
        BookingQueryOptions options)
        where TService : class
        where TImplementation : class, TService
    {
        // Validated eagerly so a bad Booking:* value fails at startup, not on the first visit.
        var baseAddress = options.GetBaseAddress();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        return services
            .AddHttpClient<TService, TImplementation>(client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = timeout;
            })
            // A redirect from Booking is an unexpected answer: following it could resend the
            // service token to another host.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            })
            // availability-service token (client credentials), cached by the singleton provider.
            // If no token can be obtained the handler throws and the request is never sent.
            .AddServiceBearerToken();
    }
}
