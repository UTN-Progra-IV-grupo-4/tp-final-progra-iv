using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Availability.Web.Authentication;

/// <summary>
/// Development authentication handler that always authenticates a fixed user.
/// It is only registered when DevelopmentAuthentication:Enabled is true in Development
/// (Program.cs refuses to start otherwise) and it refuses to authenticate anywhere else.
/// </summary>
public class DevelopmentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Development";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _environment = environment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Defense in depth: even if the scheme were registered by mistake, it never works outside Development.
        if (!_environment.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.Fail("The development authentication is only available in Development."));
        }

        var userId = _configuration["DevelopmentAuthentication:UserId"] ?? "dev-user-123";
        var userName = _configuration["DevelopmentAuthentication:UserName"] ?? "Development User";
        var email = _configuration["DevelopmentAuthentication:Email"] ?? "dev@example.com";

        // "sub" is what the controllers read first (same claim Keycloak issues); the rest mirrors the OIDC mapping.
        var claims = new List<Claim>
        {
            new Claim("sub", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
            new Claim("name", userName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
