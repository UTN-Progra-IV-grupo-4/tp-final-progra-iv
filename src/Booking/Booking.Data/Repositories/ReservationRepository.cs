using Booking.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Booking.Data.Repositories;

// Separa el acceso a datos de las operaciones de Business.
public class ReservationRepository
{
    private readonly BookingDbContext _dbContext;

    // Recibe el contexto mediante inyección de dependencias.
    public ReservationRepository(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Busca por id de solicitud; devuelve null si no encuentra la reserva.
    public async Task<Reservation?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Reservations
            // Solo consultamos: no necesitamos seguimiento de cambios.
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation => reservation.Id == requestId,
                cancellationToken);
    }
}
