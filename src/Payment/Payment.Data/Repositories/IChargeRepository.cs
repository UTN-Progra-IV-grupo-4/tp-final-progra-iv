using Payment.Data.Entities;

namespace Payment.Data.Repositories;

// Repositorio específico en vez de uno genérico: expone solo lo que el caso de uso necesita
// (nadie debería poder borrar un cobro), y Business no se entera de que abajo hay EF Core.
public interface IChargeRepository
{
    // Si Booking cancela la llamada gRPC, el token se propaga
    // y la consulta a la base se corta en vez de seguir consumiendo recursos
    Task<Charge?> GetByReservationIdAsync(Guid reservationId, CancellationToken cancellationToken = default);

    // Inserta el cobro en el acto. Devuelve false si la reserva ya tenía un cobro: qué significa
    // eso es una regla de negocio y la decide Business, no esta capa.
    Task<bool> TryAddAsync(Charge charge, CancellationToken cancellationToken = default);

    Task UpdateAsync(Charge charge, CancellationToken cancellationToken = default);
}
