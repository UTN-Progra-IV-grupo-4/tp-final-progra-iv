namespace Availability.Infrastructure.Booking;

// TODO (#11): reemplazar este registro por client credentials de availability-service.
// No se acepta un token estático en appsettings ni se omite la autenticación de Booking.
// IMPORTANT (#11, PR #51): el proveedor real debe registrarse como singleton, con caché del
// token y renovación ~60 s antes de que expire. El TryAddScoped actual iría a Keycloak en
// cada visita a la página pública (que es anónima) si se reemplazara sin esa caché.
public class PendingBookingServiceTokenProvider : IBookingServiceTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
