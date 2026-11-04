using Booking.Data.Repositories;
using Booking.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Booking.Tests.Authentication;

public class BookingApiFactory : WebApplicationFactory<Program>
{
    public StubReservationRepository Repository { get; } = new();

    protected virtual string Authority => TestTokens.Issuer;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so appsettings.Development.json does not set an authority.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Booking", "Host=unused;Database=unused");
        builder.UseSetting("Authentication:Authority", Authority);
        builder.UseSetting("Authentication:RequireHttpsMetadata", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReservationRepository>();
            services.AddSingleton<IReservationRepository>(Repository);

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

public sealed class UnconfiguredAuthorityBookingApiFactory : BookingApiFactory
{
    protected override string Authority => string.Empty;
}
