using System.Net;
using Booking.Api.Messaging;
using Booking.Business.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceAuthentication;

namespace Booking.Tests.Payments;

public sealed class GrpcPaymentChargeClientAuthenticationTests
{
    [Fact]
    public async Task ChargeAsync_SendsTheBookingServiceTokenAsBearerCredential()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateProvider(new FixedTokenProvider("service-token"), handler);
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IPaymentChargeClient>();

        // The recording handler answers with a transport failure: only the outgoing request matters.
        await Assert.ThrowsAsync<PaymentChargeException>(() => client.ChargeAsync(
            new PaymentChargeRequest(Guid.NewGuid(), 150000, "ARS", "correlation-19")));

        var authorization = Assert.Single(handler.AuthorizationHeaders);
        Assert.Equal("Bearer service-token", authorization);
    }

    [Fact]
    public async Task ChargeAsync_WhenTokenCannotBeAcquired_ReportsUnavailableWithoutCallingPayment()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateProvider(new FailingTokenProvider(), handler);
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IPaymentChargeClient>();

        var exception = await Assert.ThrowsAsync<PaymentChargeException>(() => client.ChargeAsync(
            new PaymentChargeRequest(Guid.NewGuid(), 150000, "ARS", null)));

        // No request left Booking, so no charge exists and the bounded retry is safe.
        Assert.Equal(PaymentChargeFailure.Unavailable, exception.Failure);
        Assert.Equal(0, handler.RequestCount);
    }

    private static ServiceProvider CreateProvider(IServiceTokenProvider tokenProvider, RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:Enabled"] = "true",
                ["Payment:Address"] = "http://payment.test",
                ["Payment:AmountCents"] = "150000",
                ["Payment:Currency"] = "ARS",
                ["ReservationProcessing:RetentionDuration"] = "00:10:00"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBookingMessaging(configuration);
        services.AddSingleton(tokenProvider);
        services.AddHttpClient("PaymentServiceClient")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string?> authorizationHeaders = [];
        private int requestCount;

        public IReadOnlyList<string?> AuthorizationHeaders => authorizationHeaders;

        public int RequestCount => requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            authorizationHeaders.Add(request.Headers.Authorization?.ToString());
            throw new HttpRequestException("Simulated transport failure.", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class FixedTokenProvider(string token) : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    private sealed class FailingTokenProvider : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            throw new ServiceTokenAcquisitionException("The token request failed.");
    }
}
