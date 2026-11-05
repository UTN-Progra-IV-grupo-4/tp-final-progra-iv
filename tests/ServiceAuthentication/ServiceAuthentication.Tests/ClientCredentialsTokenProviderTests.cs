using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ServiceAuthentication.Tests;

public class ClientCredentialsTokenProviderTests
{
    private const string Authority = "https://keycloak.test/realms/seat-reservation";

    private readonly FakeTimeProvider timeProvider = new();
    private readonly ScriptedHandler handler = new();

    [Fact]
    public async Task GetAccessToken_SendsClientCredentialsGrantToTheRealmTokenEndpoint()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300));
        var provider = CreateProvider();

        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{Authority}/protocol/openid-connect/token", request.Uri);
        Assert.Contains("grant_type=client_credentials", request.Body);
        Assert.Contains("client_id=availability-service", request.Body);
        Assert.Contains("client_secret=s3cret", request.Body);
    }

    [Fact]
    public async Task GetAccessToken_WithinLifetime_ReusesTheCachedToken()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300));
        var provider = CreateProvider();

        await provider.GetAccessTokenAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(200));
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessToken_InsideTheRefreshSkew_RequestsANewToken()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300));
        handler.Enqueue(TokenResponse("token-2", expiresIn: 300));
        var provider = CreateProvider(refreshSkewSeconds: 30);

        await provider.GetAccessTokenAsync(CancellationToken.None);
        // 280 s elapsed: the token is still valid for 20 s but inside the 30 s skew.
        timeProvider.Advance(TimeSpan.FromSeconds(280));
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessToken_ConcurrentCallers_ShareASingleTokenRequest()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300), delay: TimeSpan.FromMilliseconds(100));
        var provider = CreateProvider();

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => provider.GetAccessTokenAsync(CancellationToken.None)));

        Assert.All(tokens, token => Assert.Equal("token-1", token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessToken_ShortLivedToken_CapsTheSkewToHalfTheLifetime()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 20));
        var provider = CreateProvider(refreshSkewSeconds: 30);

        await provider.GetAccessTokenAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessToken_WithoutExpiresIn_DoesNotCacheTheToken()
    {
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"token-1"}""", Encoding.UTF8, "application/json")
        });
        handler.Enqueue(TokenResponse("token-2", expiresIn: 300));
        var provider = CreateProvider();

        await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", second);
    }

    [Fact]
    public async Task GetAccessToken_WhenTheEndpointFails_ThrowsWithoutLeakingTheBodyOrSecret()
    {
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"unauthorized_client","detail":"client_secret=s3cret"}""")
        });
        var logger = new CapturingLogger();
        var provider = CreateProvider(logger: logger);

        var exception = await Assert.ThrowsAsync<ServiceTokenAcquisitionException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        Assert.IsAssignableFrom<HttpRequestException>(exception);
        Assert.Contains("401", exception.Message);
        Assert.DoesNotContain("s3cret", exception.Message);
        Assert.All(logger.Messages, message => Assert.DoesNotContain("s3cret", message));
    }

    [Fact]
    public async Task GetAccessToken_AfterAFailure_RetriesInsteadOfCachingTheFailure()
    {
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300));
        var provider = CreateProvider();

        await Assert.ThrowsAsync<ServiceTokenAcquisitionException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
    }

    [Theory]
    [InlineData("""{"access_token":"","expires_in":300}""")]
    [InlineData("""{"access_token":"two words","expires_in":300}""")]
    [InlineData("""{"expires_in":300}""")]
    [InlineData("not json")]
    public async Task GetAccessToken_WithUnusableBody_Throws(string body)
    {
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider();

        await Assert.ThrowsAsync<ServiceTokenAcquisitionException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("", "availability-service", "s3cret")]
    [InlineData(Authority, "", "s3cret")]
    [InlineData(Authority, "availability-service", "")]
    [InlineData("not a url", "availability-service", "s3cret")]
    public async Task GetAccessToken_WithIncompleteConfiguration_FailsWithoutCallingKeycloak(
        string authority, string clientId, string clientSecret)
    {
        var provider = CreateProvider(authority: authority, clientId: clientId, clientSecret: clientSecret);

        await Assert.ThrowsAsync<ServiceTokenAcquisitionException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAccessToken_HttpAuthorityWhileHttpsIsRequired_RefusesToSendTheSecret()
    {
        var provider = CreateProvider(authority: "http://keycloak.test/realms/seat-reservation");

        await Assert.ThrowsAsync<ServiceTokenAcquisitionException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAccessToken_HttpAuthorityWithHttpsRequirementDisabled_IsAllowed()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300));
        var provider = CreateProvider(
            authority: "http://localhost:8080/realms/seat-reservation",
            requireHttps: false);

        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
    }

    [Fact]
    public async Task GetAccessToken_WhenTheCallerCancels_PropagatesTheCancellation()
    {
        handler.Enqueue(TokenResponse("token-1", expiresIn: 300), delay: TimeSpan.FromSeconds(5));
        var provider = CreateProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAccessTokenAsync(cancellation.Token));
    }

    private ClientCredentialsTokenProvider CreateProvider(
        string authority = Authority,
        string clientId = "availability-service",
        string clientSecret = "s3cret",
        bool requireHttps = true,
        int refreshSkewSeconds = 30,
        ILogger<ClientCredentialsTokenProvider>? logger = null)
    {
        var options = Options.Create(new ClientCredentialsOptions
        {
            Authority = authority,
            ClientId = clientId,
            ClientSecret = clientSecret,
            RequireHttpsMetadata = requireHttps,
            RefreshSkewSeconds = refreshSkewSeconds
        });

        return new ClientCredentialsTokenProvider(
            new SingleClientFactory(handler),
            options,
            timeProvider,
            logger ?? NullLogger<ClientCredentialsTokenProvider>.Instance);
    }

    private static HttpResponseMessage TokenResponse(string accessToken, int expiresIn) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""",
                Encoding.UTF8,
                "application/json")
        };

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingLogger : ILogger<ClientCredentialsTokenProvider>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
}
