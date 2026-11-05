using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ServiceAuthentication;

public static class ServiceCollectionExtensions
{
    // Registers the token provider. Credentials are validated lazily on first use so a service without
    // outgoing calls configured still starts; a missing secret fails the call, it never opens it.
    public static IServiceCollection AddServiceTokenProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ClientCredentialsOptions>()
            .Bind(configuration.GetSection(ClientCredentialsOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName);
        services.TryAddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();
        services.TryAddTransient<BearerTokenHandler>();

        return services;
    }

    // Outgoing requests of this client carry the service token (Authorization: Bearer ...).
    public static IHttpClientBuilder AddServiceBearerToken(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<BearerTokenHandler>();
}
