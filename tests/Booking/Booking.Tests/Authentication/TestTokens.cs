using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Tests.Authentication;

// Mints tokens signed with a test-only symmetric key; Keycloak is never called.
internal static class TestTokens
{
    public const string Issuer = "http://keycloak.test/realms/seat-reservation";
    public const string Audience = "booking-service";

    public static readonly SymmetricSecurityKey SigningKey = CreateKey("booking-tests-signing-key-0123456789");

    public static SymmetricSecurityKey CreateKey(string secret) =>
        new(Encoding.UTF8.GetBytes(secret));

    public static string Create(
        string authorizedParty = "availability-service",
        string audience = Audience,
        string issuer = Issuer,
        SecurityKey? signingKey = null,
        TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now + (lifetime ?? TimeSpan.FromMinutes(5));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            // An already expired token must also start in the past, before the default clock skew.
            NotBefore = expiresAt < now ? expiresAt.AddMinutes(-5) : now,
            Expires = expiresAt,
            Claims = new Dictionary<string, object> { ["azp"] = authorizedParty },
            SigningCredentials = new SigningCredentials(
                signingKey ?? SigningKey,
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
