using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Payment.Tests.Authentication;

public class PaymentApiFactory : WebApplicationFactory<Program>
{
    protected virtual string Authority => TestTokens.Issuer;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so appsettings.Development.json does not set an authority.
        builder.UseEnvironment("Testing");
        // The database is never reached: the tests send requests that Payment rejects before Business.
        builder.UseSetting("ConnectionStrings:Payment", "Host=unused;Database=unused");
        builder.UseSetting("Authentication:Authority", Authority);
        builder.UseSetting("Authentication:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            if (string.IsNullOrEmpty(Authority))
            {
                return;
            }

            // Replace the discovery document download with a static test key.
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                    configuration.SigningKeys.Add(TestTokens.SigningKey);
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
        });
    }
}

public sealed class UnconfiguredAuthorityPaymentApiFactory : PaymentApiFactory
{
    protected override string Authority => string.Empty;
}
