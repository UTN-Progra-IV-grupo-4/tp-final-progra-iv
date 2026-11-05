namespace ServiceAuthentication;

// Credentials of the calling service (a confidential Keycloak client with client credentials enabled).
public sealed class ClientCredentialsOptions
{
    public const string SectionName = "ServiceCredentials";

    // Keycloak realm URL, for example http://localhost:8080/realms/seat-reservation.
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    // Never versioned: user-secrets or the ServiceCredentials__ClientSecret environment variable.
    public string ClientSecret { get; set; } = string.Empty;

    // The secret travels in the token request, so plain HTTP is only accepted when this is explicitly
    // disabled (local development).
    public bool RequireHttpsMetadata { get; set; } = true;

    // A cached token is renewed this long before it expires, so a call never leaves with a token
    // that dies in flight. Capped at half of the token lifetime.
    public int RefreshSkewSeconds { get; set; } = 30;

    public int RequestTimeoutSeconds { get; set; } = 10;
}
