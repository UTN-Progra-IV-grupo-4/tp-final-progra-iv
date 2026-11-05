using System.Security.Claims;
using System.Text.Encodings.Web;
using Availability.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Availability.Tests;

// Real pipeline against the test PostgreSQL, with Booking replaced by a configurable stub
// and a header-based authentication: "X-Test-User" is the user id, no header means anonymous.
public sealed class ReservationStatusWebFactory(
    string connectionString,
    ConfigurableBookingStatusClient bookingStatusClient,
    IReadOnlyDictionary<string, string>? settings = null,
    IMessagePublisher? messagePublisher = null) : WebApplicationFactory<Program>
{
    public const string UserHeader = "X-Test-User";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Availability", connectionString);
        foreach (var setting in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBookingStatusClient>();
            services.AddSingleton<IBookingStatusClient>(bookingStatusClient);

            if (messagePublisher is not null)
            {
                // The broker is replaced by a capturing (or failing) publisher.
                services.RemoveAll<IMessagePublisher>();
                services.AddSingleton(messagePublisher);
            }

            // Only the authenticate scheme changes: challenges still go through the real cookie
            // scheme, so an anonymous request is answered exactly as in production.
            services
                .AddAuthentication(options => options.DefaultAuthenticateScheme = HeaderAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                    HeaderAuthenticationHandler.SchemeName, _ => { });
        });
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        public const string SchemeName = "TestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId) || string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", userId.ToString())], SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

// Answers with the configured function and records which ids Booking was asked about.
public sealed class ConfigurableBookingStatusClient : IBookingStatusClient
{
    public Func<Guid, ReservationStatusResult?> Answer { get; set; } = _ => null;

    public List<Guid> RequestedIds { get; } = [];

    public Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        RequestedIds.Add(requestId);
        return Task.FromResult(Answer(requestId));
    }
}
