using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using ServiceAuthentication;

namespace Availability.Tests;

// Service-to-service tokens must not change how end users authenticate: the cookie scheme stays the
// default (#14 plugs the OIDC login on top of it).
[Collection(PostgreSqlCollection.Name)]
public sealed class ServiceTokenWiringTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Host_RegistersTheServiceTokenProviderAndKeepsTheCookieDefault()
    {
        using var factory = new ReservationStatusWebFactory(
            fixture.ConnectionString,
            new ConfigurableBookingStatusClient());

        Assert.IsType<ClientCredentialsTokenProvider>(factory.Services.GetRequiredService<IServiceTokenProvider>());

        var schemeProvider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = await schemeProvider.GetDefaultChallengeSchemeAsync();
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, defaultScheme?.Name);
    }
}
