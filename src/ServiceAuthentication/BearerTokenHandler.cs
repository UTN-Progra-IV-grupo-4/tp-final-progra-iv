using System.Net.Http.Headers;

namespace ServiceAuthentication;

// Attaches the service token to every outgoing request. Works for plain HttpClient and for gRPC
// clients registered through AddGrpcClient, because both go through the HttpClient pipeline.
public sealed class BearerTokenHandler(IServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
