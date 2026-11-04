namespace Availability.Infrastructure.Booking;

// Adaptador de infraestructura: Application no necesita conocer JWT ni Keycloak.
public interface IBookingServiceTokenProvider
{
    // Debe devolver el access_token de availability-service sin el prefijo Bearer,
    // nunca uno del usuario. El cliente HTTP agrega el encabezado correspondiente.
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}
