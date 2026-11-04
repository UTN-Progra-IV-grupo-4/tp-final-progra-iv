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

    // Pasa a Pending un cobro Failed, o uno Pending reclamado antes de staleBefore (el proceso que lo
    // tenía se cayó), y registra claimedAt. Devuelve false si otro pedido ya lo tomó: es el mismo
    // "tomar el turno antes de cobrar" que TryAddAsync, pero sobre una fila existente.
    Task<bool> TryReclaimAsync(
        Guid chargeId, DateTime staleBefore, DateTime claimedAt, CancellationToken cancellationToken = default);

    // Guarda el resultado solo si el reclamo sigue siendo de este pedido (ClaimedAt sin cambios).
    // Devuelve false si otro pedido lo reclamó mientras tanto: esa decisión es de Business.
    Task<bool> TryUpdateAsync(Charge charge, CancellationToken cancellationToken = default);
}
