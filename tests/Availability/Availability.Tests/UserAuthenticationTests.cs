using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Availability.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Availability.Tests;

// Real pipeline of #14: the fixed-user login must exist only in Development, the API keeps answering
// 401 instead of redirecting, and the MVC login only redirects to local URLs.
public sealed class UserAuthenticationTests
{
    private const string FakeEndSessionEndpoint = "https://keycloak.test/realms/seat-reservation/protocol/openid-connect/logout";
    private const string FakeAuthorizationEndpoint = "https://keycloak.test/realms/seat-reservation/protocol/openid-connect/auth";

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool? developmentAuthenticationEnabled,
        bool stubOpenIdConnectMetadata = false)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Outside Development the Keycloak metadata must be served over HTTPS.
            builder.UseSetting("Keycloak:Authority", "https://keycloak.test/realms/seat-reservation");
            if (developmentAuthenticationEnabled is { } enabled)
            {
                builder.UseSetting("DevelopmentAuthentication:Enabled", enabled ? "true" : "false");
            }

            if (stubOpenIdConnectMetadata)
            {
                // The provider metadata would otherwise be downloaded from Keycloak on the first challenge.
                builder.ConfigureTestServices(services => services.PostConfigure<OpenIdConnectOptions>(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    options => options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration
                        {
                            Issuer = "https://keycloak.test/realms/seat-reservation",
                            AuthorizationEndpoint = FakeAuthorizationEndpoint,
                            EndSessionEndpoint = FakeEndSessionEndpoint
                        })));
            }
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Reads the anti-forgery token (and keeps its cookie in the client) from the login page.
    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var page = await client.GetStringAsync("/Account/Login");
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The login page must render an anti-forgery token.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string token) =>
        await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

    private static async Task<bool> HasSchemeAsync(WebApplicationFactory<Program> factory, string schemeName)
    {
        var schemeProvider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        return await schemeProvider.GetSchemeAsync(schemeName) is not null;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Startup_DevelopmentAuthenticationEnabledOutsideDevelopment_Fails(string environment)
    {
        using var factory = CreateFactory(environment, developmentAuthenticationEnabled: true);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("DevelopmentAuthentication:Enabled", exception.ToString());
    }

    [Fact]
    public async Task Startup_Production_RegistersOpenIdConnectAndNotTheDevelopmentScheme()
    {
        using var factory = CreateFactory("Production", developmentAuthenticationEnabled: null);

        Assert.False(await HasSchemeAsync(factory, DevelopmentAuthenticationHandler.SchemeName));
        Assert.True(await HasSchemeAsync(factory, OpenIdConnectDefaults.AuthenticationScheme));
    }

    [Fact]
    public async Task Startup_DevelopmentWithFlagDisabled_DoesNotRegisterTheDevelopmentScheme()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: false);

        Assert.False(await HasSchemeAsync(factory, DevelopmentAuthenticationHandler.SchemeName));
        Assert.True(await HasSchemeAsync(factory, OpenIdConnectDefaults.AuthenticationScheme));
    }

    [Fact]
    public async Task Startup_DevelopmentWithFlagEnabled_RegistersTheDevelopmentScheme()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);

        Assert.True(await HasSchemeAsync(factory, DevelopmentAuthenticationHandler.SchemeName));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Development", false)]
    [InlineData("Production", null)]
    public async Task ApiReservations_WithoutSession_Returns401WithoutRedirect(string environment, bool? developmentAuthenticationEnabled)
    {
        using var factory = CreateFactory(environment, developmentAuthenticationEnabled);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/reservations",
            new { eventId = Guid.NewGuid(), seatId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ReservationStatusPage_WithoutSession_RedirectsToTheLoginPageKeepingTheReturnUrl()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: false);
        using var client = CreateClient(factory);
        var requestId = Guid.NewGuid();

        var response = await client.GetAsync($"/reservas/{requestId}/estado");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/Account/Login", location);
        Assert.Contains(Uri.EscapeDataString($"/reservas/{requestId}/estado"), location);
    }

    [Fact]
    public async Task Login_Production_ChallengesKeycloakWithAuthorizationCodeAndPkce()
    {
        using var factory = CreateFactory("Production", developmentAuthenticationEnabled: null, stubOpenIdConnectMetadata: true);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/Account/Login?returnUrl=%2Feventos", token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(FakeAuthorizationEndpoint, location.ToString());
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("availability-web", query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.Null(query["client_secret"]);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_Returns400()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);
        using var client = CreateClient(factory);

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/eventos", "/eventos")]
    [InlineData("https://evil.example/phish", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("", "/")]
    public async Task Login_Development_OnlyRedirectsToLocalReturnUrls(string returnUrl, string expectedLocation)
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}", token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Cookies="));
    }

    [Fact]
    public async Task Login_Development_IgnoresIdentityValuesSentByTheClient()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await client.PostAsync("/Account/Login?userId=attacker&sub=attacker", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["userId"] = "attacker",
            ["sub"] = "attacker"
        }));

        // The identity is always the configured fixed user; there is no parameter that can change it.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var authenticationCookie = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Cookies="));
        Assert.DoesNotContain("attacker", authenticationCookie);
    }

    [Fact]
    public async Task Logout_Get_IsNotAvailable()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Account/Logout");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"A GET must not log out, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Logout_PostWithoutAntiforgeryToken_Returns400()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);
        using var client = CreateClient(factory);

        var response = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_Development_ClearsTheAuthenticationCookie()
    {
        using var factory = CreateFactory("Development", developmentAuthenticationEnabled: true);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);
        await PostFormAsync(client, "/Account/Login", token);
        // The anti-forgery token is bound to the user: a signed-in page issues the one the logout form carries.
        var signedInToken = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/Account/Logout", signedInToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(".AspNetCore.Cookies=;") && cookie.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Logout_Production_RedirectsToTheKeycloakEndSession()
    {
        using var factory = CreateFactory("Production", developmentAuthenticationEnabled: null, stubOpenIdConnectMetadata: true);
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryTokenAsync(client);

        var response = await PostFormAsync(client, "/Account/Logout", token);

        // Redirecting home instead would leave the Keycloak session alive (the next login would skip the password).
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(FakeEndSessionEndpoint, response.Headers.Location?.ToString() ?? string.Empty);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task DevelopmentHandler_AuthenticatesTheFixedUserOnlyInDevelopment(string environment, bool expectedSuccess)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentAuthentication:UserId"] = "fixed-user-1",
                ["DevelopmentAuthentication:UserName"] = "Fixed User",
                ["DevelopmentAuthentication:Email"] = "fixed@example.com"
            })
            .Build();
        var hostEnvironment = new TestHostEnvironment { EnvironmentName = environment };
        var handler = new DevelopmentAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            System.Text.Encodings.Web.UrlEncoder.Default,
            configuration,
            hostEnvironment);
        await handler.InitializeAsync(
            new AuthenticationScheme(DevelopmentAuthenticationHandler.SchemeName, null, typeof(DevelopmentAuthenticationHandler)),
            new DefaultHttpContext());

        var result = await handler.AuthenticateAsync();

        Assert.Equal(expectedSuccess, result.Succeeded);
        if (expectedSuccess)
        {
            // Same claims the controllers read for the ownership checks of reservations.
            Assert.Equal("fixed-user-1", result.Principal!.FindFirst("sub")?.Value);
            Assert.Equal("fixed-user-1", result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        }
        else
        {
            Assert.Null(result.Principal);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Availability.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
