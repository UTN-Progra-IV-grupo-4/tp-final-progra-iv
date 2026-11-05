using ServiceAuthentication;

namespace Availability.Tests;

// Invented credential used only by tests; it is never sent to a real Keycloak or Booking.
internal sealed class FakeServiceTokenProvider(string? token = "test-service-token") : IServiceTokenProvider
{
    public int Calls { get; private set; }

    // A null token simulates Keycloak being unreachable or the secret being wrong.
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return token is null
            ? Task.FromException<string>(new ServiceTokenAcquisitionException("No service token available."))
            : Task.FromResult(token);
    }
}

internal sealed class BookingResponseHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public Uri? RequestUri { get; private set; }
    public string? Authorization { get; private set; }

    // Values of x-correlation-id on the last request (empty when the header was not sent).
    public string[] CorrelationIds { get; private set; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        RequestUri = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();
        CorrelationIds = request.Headers.TryGetValues("x-correlation-id", out var values) ? values.ToArray() : [];
        return respond(request, cancellationToken);
    }
}

internal static class BookingHttpTestFactory
{
    // Same pipeline as production for the service token: the shared bearer handler in front of the stub.
    public static HttpClient CreateClient(HttpMessageHandler booking, IServiceTokenProvider tokenProvider,
        string baseAddress = "http://booking.test/prefix/", TimeSpan? timeout = null)
        => new(new BearerTokenHandler(tokenProvider) { InnerHandler = booking })
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = timeout ?? TimeSpan.FromSeconds(2)
        };
}
