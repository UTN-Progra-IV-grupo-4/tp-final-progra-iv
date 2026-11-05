namespace ServiceAuthentication;

// Derives from HttpRequestException so callers that already treat transport failures as "dependency
// unavailable" handle a failed token request the same way. The message never contains secrets or tokens.
public sealed class ServiceTokenAcquisitionException : HttpRequestException
{
    public ServiceTokenAcquisitionException(string message)
        : base(message)
    {
    }

    public ServiceTokenAcquisitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
