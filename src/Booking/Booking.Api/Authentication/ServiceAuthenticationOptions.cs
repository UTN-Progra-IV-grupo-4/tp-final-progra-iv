namespace Booking.Api.Authentication;

internal sealed class ServiceAuthenticationOptions
{
    public const string SectionName = "Authentication";

    // Keycloak realm URL. Empty means "not configured": protected endpoints answer 401.
    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = "booking-service";

    // Only tokens issued to this client (azp claim) may call Booking.
    public string AllowedClientId { get; set; } = "availability-service";

    public bool RequireHttpsMetadata { get; set; } = true;
}
