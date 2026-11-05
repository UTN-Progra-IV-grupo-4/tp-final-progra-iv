using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceAuthentication.Tests;

public class BearerTokenHandlerTests
{
    [Fact]
    public async Task Send_AttachesTheServiceTokenAsABearerHeader()
    {
        var downstream = new ScriptedHandler();
        downstream.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(
            new BearerTokenHandler(new FixedTokenProvider("token-1")) { InnerHandler = downstream });

        await client.GetAsync("https://booking.test/api/events/1/occupied-seats");

        Assert.Equal("Bearer token-1", Assert.Single(downstream.Requests).Authorization);
    }

    [Fact]
    public async Task Send_OverridesAnAuthorizationHeaderSetByTheCaller()
    {
        var downstream = new ScriptedHandler();
        downstream.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(
            new BearerTokenHandler(new FixedTokenProvider("token-1")) { InnerHandler = downstream });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://booking.test/x");
        request.Headers.Authorization = new("Bearer", "end-user-token");

        await client.SendAsync(request);

        Assert.Equal("Bearer token-1", Assert.Single(downstream.Requests).Authorization);
    }

    [Fact]
    public async Task Send_WhenNoTokenCanBeObtained_FailsWithoutSendingTheRequest()
    {
        var downstream = new ScriptedHandler();
        using var client = new HttpClient(
            new BearerTokenHandler(new FailingTokenProvider()) { InnerHandler = downstream });

        await Assert.ThrowsAsync<ServiceTokenAcquisitionException>(
            () => client.GetAsync("https://booking.test/x"));

        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task AddServiceBearerToken_WiresTheHandlerIntoAClientFromTheContainer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceCredentials:Authority"] = "https://keycloak.test/realms/seat-reservation",
                ["ServiceCredentials:ClientId"] = "availability-service",
                ["ServiceCredentials:ClientSecret"] = "s3cret"
            })
            .Build();
        var downstream = new ScriptedHandler();
        downstream.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceTokenProvider(configuration);
        services.AddSingleton<IServiceTokenProvider>(new FixedTokenProvider("token-1"));
        services
            .AddHttpClient("booking")
            .AddServiceBearerToken()
            .ConfigurePrimaryHttpMessageHandler(() => downstream);
        await using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("booking");
        await client.GetAsync("https://booking.test/x");

        Assert.Equal("Bearer token-1", Assert.Single(downstream.Requests).Authorization);
    }

    [Fact]
    public void AddServiceTokenProvider_BindsTheServiceCredentialsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceCredentials:Authority"] = "https://keycloak.test/realms/seat-reservation",
                ["ServiceCredentials:ClientId"] = "booking-service",
                ["ServiceCredentials:ClientSecret"] = "s3cret"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceTokenProvider(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ClientCredentialsOptions>>().Value;

        Assert.Equal("booking-service", options.ClientId);
        Assert.True(options.RequireHttpsMetadata);
        Assert.IsType<ClientCredentialsTokenProvider>(provider.GetRequiredService<IServiceTokenProvider>());
    }

    private sealed class FixedTokenProvider(string token) : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    private sealed class FailingTokenProvider : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            throw new ServiceTokenAcquisitionException("no token");
    }
}
