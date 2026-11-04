namespace Availability.Infrastructure.Booking;

public class BookingQueryOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;

    public Uri? GetBaseAddress()
    {
        if (TimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("Booking:TimeoutSeconds debe estar entre 1 y 30.");
        }

        // Sin URL podemos abrir el catálogo, pero no afirmar disponibilidad.
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Booking:BaseUrl debe ser una URL HTTP/HTTPS sin credenciales, query ni fragmento.");
        }

        // La barra final conserva un posible prefijo de ruta al combinar el endpoint.
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
