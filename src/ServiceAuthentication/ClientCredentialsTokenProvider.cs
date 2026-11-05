using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServiceAuthentication;

public sealed class ClientCredentialsTokenProvider : IServiceTokenProvider, IDisposable
{
    public const string HttpClientName = "service-token-endpoint";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<ClientCredentialsOptions> options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ClientCredentialsTokenProvider> logger;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    // Immutable snapshot swapped atomically, so the fast path needs no lock.
    private volatile CachedToken? cachedToken;

    public ClientCredentialsTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<ClientCredentialsOptions> options,
        TimeProvider timeProvider,
        ILogger<ClientCredentialsTokenProvider> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (TryGetValidToken(out var token))
        {
            return token;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while this one waited: one token request per expiry.
            if (TryGetValidToken(out token))
            {
                return token;
            }

            var acquired = await RequestTokenAsync(cancellationToken);
            cachedToken = acquired;
            return acquired.AccessToken;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Dispose() => refreshLock.Dispose();

    private bool TryGetValidToken(out string token)
    {
        var snapshot = cachedToken;
        if (snapshot is not null && timeProvider.GetUtcNow() < snapshot.RefreshAt)
        {
            token = snapshot.AccessToken;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<CachedToken> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        ValidateSettings(settings);

        var tokenEndpoint = $"{settings.Authority.TrimEnd('/')}/protocol/openid-connect/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Status code only: the body can echo request details.
                logger.LogWarning(
                    "Token request for client {ClientId} failed with HTTP {StatusCode}.",
                    settings.ClientId, (int)response.StatusCode);
                throw new ServiceTokenAcquisitionException(
                    $"The token endpoint answered HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(timeout.Token);
            if (body is null ||
                string.IsNullOrWhiteSpace(body.AccessToken) ||
                body.AccessToken.Any(char.IsWhiteSpace))
            {
                throw new ServiceTokenAcquisitionException("The token endpoint returned no usable access token.");
            }

            return ToCachedToken(body.AccessToken, body.ExpiresInSeconds, settings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Token request for client {ClientId} timed out.", settings.ClientId);
            throw new ServiceTokenAcquisitionException("The token request timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
                                          && exception is not ServiceTokenAcquisitionException)
        {
            logger.LogWarning(
                "Token request for client {ClientId} failed: {ErrorType}.",
                settings.ClientId, exception.GetType().Name);
            throw new ServiceTokenAcquisitionException("The token request failed.", exception);
        }
    }

    private CachedToken ToCachedToken(string accessToken, int expiresInSeconds, ClientCredentialsOptions settings)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Max(expiresInSeconds, 0));
        var skew = TimeSpan.FromSeconds(Math.Max(settings.RefreshSkewSeconds, 0));
        if (skew > lifetime / 2)
        {
            skew = lifetime / 2;
        }

        // Without a positive expires_in the refresh instant is "now": the token is used but never reused.
        return new CachedToken(accessToken, timeProvider.GetUtcNow() + lifetime - skew);
    }

    private static void ValidateSettings(ClientCredentialsOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Authority) ||
            string.IsNullOrWhiteSpace(settings.ClientId) ||
            string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new ServiceTokenAcquisitionException(
                $"{ClientCredentialsOptions.SectionName}:Authority, ClientId and ClientSecret must be configured.");
        }

        if (!Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            throw new ServiceTokenAcquisitionException(
                $"{ClientCredentialsOptions.SectionName}:Authority must be an absolute http(s) URL.");
        }

        if (settings.RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new ServiceTokenAcquisitionException(
                $"{ClientCredentialsOptions.SectionName}:Authority must use HTTPS because the client secret is sent to it.");
        }
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAt);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }
    }
}
