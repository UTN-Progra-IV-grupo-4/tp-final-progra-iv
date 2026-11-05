using Availability.Infrastructure.Booking;

namespace Availability.Tests;

// Credencial inventada y exclusiva de tests; no se usa contra Keycloak ni Booking real.
internal sealed class FixedBookingTokenProvider : IBookingServiceTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>("test-service-token");
}

internal sealed class BookingResponseHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public Uri? RequestUri { get; private set; }
    public string? Authorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        RequestUri = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();
        return respond(request, cancellationToken);
    }
}
