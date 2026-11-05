using System.Net;
using System.Net.Http.Json;
using Availability.Application.Abstractions;
using Availability.Application.Events;
using Availability.Infrastructure;
using Availability.Infrastructure.Booking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceAuthentication;

namespace Availability.Tests;

// The two Booking clients (#13 occupancy, #16 status) built through the production registration:
// shared handler pipeline, shared service token provider.
public sealed class BookingClientPipelineTests
{
    private static readonly Guid EventId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid RequestId = Guid.Parse("c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32");

    [Fact]
    public async Task BothClients_SendTheServiceBearerToBooking()
    {
        var authorizations = new List<string?>();
        using var booking = new BookingResponseHandler((request, _) =>
        {
            authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(BookingAnswer(request));
        });
        await using var provider = BuildProvider("http://booking.test/", new FakeServiceTokenProvider(), booking);
        using var scope = provider.CreateScope();

        var occupancy = await scope.ServiceProvider.GetRequiredService<ISeatOccupancyQuery>()
            .GetOccupiedSeatsAsync(EventId, default);
        var status = await scope.ServiceProvider.GetRequiredService<IBookingStatusClient>().GetStatusAsync(RequestId);

        Assert.True(occupancy.IsConfirmed);
        Assert.Equal(ReservationStatus.Confirmed, status?.Status);
        Assert.Equal(["Bearer test-service-token", "Bearer test-service-token"], authorizations);
    }

    [Fact]
    public async Task TokenFailure_SendsNothingToBooking_OccupancyIsUnknown_StatusIsUnavailable()
    {
        using var booking = new BookingResponseHandler((request, _) => Task.FromResult(BookingAnswer(request)));
        await using var provider = BuildProvider("http://booking.test/", new FakeServiceTokenProvider(token: null), booking);
        using var scope = provider.CreateScope();

        var occupancy = await scope.ServiceProvider.GetRequiredService<ISeatOccupancyQuery>()
            .GetOccupiedSeatsAsync(EventId, default);
        await Assert.ThrowsAsync<BookingUnavailableException>(
            () => scope.ServiceProvider.GetRequiredService<IBookingStatusClient>().GetStatusAsync(RequestId));

        Assert.False(occupancy.IsConfirmed);
        Assert.Empty(occupancy.SeatIds);
        Assert.Equal(0, booking.Calls);
    }

    [Fact]
    public async Task RealTokenProvider_RequestsOneTokenForManyCallsAcrossScopesAndClients()
    {
        var authorizations = new List<string?>();
        using var booking = new BookingResponseHandler((request, _) =>
        {
            authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(BookingAnswer(request));
        });
        using var tokenEndpoint = new BookingResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new Dictionary<string, object> { ["access_token"] = "cached-token", ["expires_in"] = 300 })
        }));
        await using var provider = BuildProvider("http://booking.test/", tokenProvider: null, booking, tokenEndpoint);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Each public page view is a new scope: the cache must not depend on the request scope.
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISeatOccupancyQuery>().GetOccupiedSeatsAsync(EventId, default);
            await scope.ServiceProvider.GetRequiredService<IBookingStatusClient>().GetStatusAsync(RequestId);
        }

        Assert.Equal(1, tokenEndpoint.Calls);
        Assert.Equal(6, booking.Calls);
        Assert.All(authorizations, authorization => Assert.Equal("Bearer cached-token", authorization));
    }

    [Fact]
    public async Task TokenProvider_IsASingletonSharedByBothClients()
    {
        await using var provider = BuildProvider("http://booking.test/", tokenProvider: null, bookingHandler: null);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.IsType<ClientCredentialsTokenProvider>(firstScope.ServiceProvider.GetRequiredService<IServiceTokenProvider>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<IServiceTokenProvider>(),
            secondScope.ServiceProvider.GetRequiredService<IServiceTokenProvider>());
    }

    [Fact]
    public async Task Redirects_AreNotFollowed_ByEitherClient()
    {
        // A real socket is needed: the redirect policy lives in the primary handler that tests usually replace.
        await using var server = await RedirectingServer.StartAsync();
        await using var provider = BuildProvider(server.BaseUrl, new FakeServiceTokenProvider(), bookingHandler: null);
        using var scope = provider.CreateScope();

        var occupancy = await scope.ServiceProvider.GetRequiredService<ISeatOccupancyQuery>()
            .GetOccupiedSeatsAsync(EventId, default);
        await Assert.ThrowsAsync<BookingUnavailableException>(
            () => scope.ServiceProvider.GetRequiredService<IBookingStatusClient>().GetStatusAsync(RequestId));

        Assert.False(occupancy.IsConfirmed);
        Assert.Equal(2, server.RedirectingHits);
        Assert.Equal(0, server.TargetHits);
    }

    private static HttpResponseMessage BookingAnswer(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { requestId = RequestId, status = "Confirmed", rejectionReason = (string?)null })
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { eventId = EventId, seatIds = Array.Empty<Guid>() })
            };

    // tokenProvider null => the real ClientCredentialsTokenProvider against an invented, in-memory Keycloak.
    private static ServiceProvider BuildProvider(
        string baseUrl,
        IServiceTokenProvider? tokenProvider,
        HttpMessageHandler? bookingHandler,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (tokenProvider is null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceCredentials:Authority"] = "http://keycloak.test/realms/test",
                ["ServiceCredentials:ClientId"] = "availability-service",
                ["ServiceCredentials:ClientSecret"] = "invented-test-secret",
                ["ServiceCredentials:RequireHttpsMetadata"] = "false"
            }).Build();
            services.AddServiceTokenProvider(configuration);
            if (tokenEndpointHandler is not null)
            {
                services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => tokenEndpointHandler);
            }
        }
        else
        {
            services.AddSingleton(tokenProvider);
            services.AddTransient<BearerTokenHandler>();
        }

        var options = new BookingQueryOptions { BaseUrl = baseUrl };
        services.AddEventQueries(useDemoData: false, options);
        services.AddInfrastructure("Host=localhost", bookingOptions: options);
        if (bookingHandler is not null)
        {
            // Only the socket layer is replaced; the bearer handler of the shared registration stays in the pipeline.
            services.AddHttpClient<ISeatOccupancyQuery, BookingSeatOccupancyQuery>()
                .ConfigurePrimaryHttpMessageHandler(() => bookingHandler);
            services.AddHttpClient<IBookingStatusClient, BookingStatusClient>()
                .ConfigurePrimaryHttpMessageHandler(() => bookingHandler);
        }

        return services.BuildServiceProvider();
    }

    // Kestrel on a free loopback port: every Booking path answers 302 to /elsewhere, which counts hits.
    private sealed class RedirectingServer : IAsyncDisposable
    {
        private readonly WebApplication app;
        private int redirectingHits;
        private int targetHits;

        private RedirectingServer(WebApplication app, string baseUrl)
        {
            this.app = app;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }
        public int RedirectingHits => Volatile.Read(ref redirectingHits);
        public int TargetHits => Volatile.Read(ref targetHits);

        public static async Task<RedirectingServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            RedirectingServer? server = null;
            app.MapGet("/elsewhere", () =>
            {
                Interlocked.Increment(ref server!.targetHits);
                return Results.Ok();
            });
            app.MapFallback(context =>
            {
                Interlocked.Increment(ref server!.redirectingHits);
                context.Response.Redirect("/elsewhere");
                return Task.CompletedTask;
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            server = new RedirectingServer(app, address);
            return server;
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
