namespace Availability.Infrastructure.Booking;

// TODO (#11): reemplazar este registro por client credentials de availability-service.
// No se acepta un token estático en appsettings ni se omite la autenticación de Booking.
public class PendingBookingServiceTokenProvider : IBookingServiceTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
