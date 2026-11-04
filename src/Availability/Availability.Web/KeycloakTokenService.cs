using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace Availability.Web;

public class KeycloakTokenService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public KeycloakTokenService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> GetServiceTokenAsync()
    {
        var authority = _configuration["Keycloak:Authority"]
            ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
            ?? "http://localhost:8080/realms/seat-reservation";
        var clientId = _configuration["Keycloak:ServiceClientId"]
            ?? Environment.GetEnvironmentVariable("KEYCLOAK_AVAILABILITY_CLIENT_ID")
            ?? "availability-service";
        var clientSecret = _configuration["Keycloak:ServiceClientSecret"]
            ?? Environment.GetEnvironmentVariable("KEYCLOAK_AVAILABILITY_CLIENT_SECRET");

        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Keycloak service client secret is not configured. Set Keycloak:ServiceClientSecret in appsettings.json or KEYCLOAK_AVAILABILITY_CLIENT_SECRET environment variable.");
        }

        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "scope", "openid" }
        };

        var response = await _httpClient.PostAsync(
            $"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(parameters));

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return content?.AccessToken ?? throw new InvalidOperationException("No access token received");
    }

    private record TokenResponse(string AccessToken, int ExpiresIn, string TokenType);
}
