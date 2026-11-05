namespace ServiceAuthentication;

public interface IServiceTokenProvider
{
    // Returns the raw access token of the calling service (no "Bearer " prefix). Tokens are cached and
    // renewed before they expire. Throws ServiceTokenAcquisitionException when none can be obtained.
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
