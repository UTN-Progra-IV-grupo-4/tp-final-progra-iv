using Contracts.Payment;
using Grpc.Core;
using Grpc.Net.Client;

namespace Payment.Tests.Authentication;

// Real HTTP/2 pipeline (TestServer) with the real authentication middleware. A request that passes
// authorization reaches the gRPC service, which rejects the deliberately invalid body with
// INVALID_ARGUMENT before touching Business or the database; that is how "allowed" is observed.
public class ChargeAuthenticationTests : IClassFixture<PaymentApiFactory>
{
    private readonly PaymentApiFactory factory;

    public ChargeAuthenticationTests(PaymentApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Charge_WithoutToken_IsUnauthenticated()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token: null));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithWrongAudience_IsUnauthenticated()
    {
        var token = TestTokens.Create(audience: "booking-service");

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithWrongIssuer_IsUnauthenticated()
    {
        var token = TestTokens.Create(issuer: "http://other.test/realms/seat-reservation");

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithExpiredToken_IsUnauthenticated()
    {
        var token = TestTokens.Create(lifetime: TimeSpan.FromMinutes(-10));

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithWrongSignature_IsUnauthenticated()
    {
        var token = TestTokens.Create(
            signingKey: TestTokens.CreateKey("another-signing-key-0123456789-abcdef"));

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Theory]
    [InlineData("availability-service")]
    [InlineData("payment-service")]
    public async Task Charge_WithTokenOfAnotherClient_IsPermissionDenied(string authorizedParty)
    {
        var token = TestTokens.Create(authorizedParty: authorizedParty);

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithBookingServiceToken_ReachesTheService()
    {
        var token = TestTokens.Create(authorizedParty: "booking-service");

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(factory, token));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_WithoutConfiguredAuthority_IsUnauthenticated()
    {
        await using var unconfiguredFactory = new UnconfiguredAuthorityPaymentApiFactory();
        var token = TestTokens.Create();

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(unconfiguredFactory, token));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    private static async Task CallAsync(PaymentApiFactory apiFactory, string? token)
    {
        using var httpClient = apiFactory.CreateDefaultClient(new Http2Handler());
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
        var client = new PaymentService.PaymentServiceClient(channel);

        var headers = new Metadata();
        if (token is not null)
        {
            headers.Add("Authorization", $"Bearer {token}");
        }

        // Empty reservation_id: invalid on purpose, see the class comment.
        await client.ChargeAsync(
            new ChargeRequest { ReservationId = string.Empty, AmountCents = 1, Currency = "ARS" },
            headers);
    }

    // TestServer answers HTTP/2 only when the request asks for it.
    private sealed class Http2Handler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Version = new Version(2, 0);
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
