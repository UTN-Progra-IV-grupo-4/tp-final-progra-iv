using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payment.Data.Entities;

namespace Payment.Data.Repositories;

public class ChargeRepository(PaymentDbContext db) : IChargeRepository
{
    public Task<Charge?> GetByReservationIdAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        db.Charges.FirstOrDefaultAsync(c => c.ReservationId == reservationId, cancellationToken);

    public async Task<bool> TryAddAsync(Charge charge, CancellationToken cancellationToken = default)
    {
        db.Charges.Add(charge);
        try
        {
            // Se guarda ya, antes de cobrar: así el índice único frena los reintentos simultáneos.
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        // Solo la violación de unicidad: cualquier otro error de base tiene que subir.
        // Se traduce a bool para que Business no dependa de los códigos de error de PostgreSQL.
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Si queda en estado Added, el próximo SaveChanges del mismo pedido reintentaría el insert.
            db.Entry(charge).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryReclaimAsync(
        Guid chargeId, DateTime staleBefore, DateTime claimedAt, CancellationToken cancellationToken = default)
    {
        // UPDATE condicional en un solo paso: la base evalúa el WHERE con la fila bloqueada, así que
        // entre dos reintentos simultáneos solo uno lo ve reclamable. Leer y después guardar dejaría a ambos cobrar.
        // Va directo a la base sin pasar por el change tracker: la entidad en memoria no se entera.
        var affected = await db.Charges
            .Where(c => c.Id == chargeId &&
                (c.Status == ChargeStatus.Failed ||
                 (c.Status == ChargeStatus.Pending && c.ClaimedAt < staleBefore)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ChargeStatus.Pending)
                .SetProperty(c => c.ClaimedAt, claimedAt)
                .SetProperty(c => c.FailureReason, (string?)null)
                .SetProperty(c => c.CompletedAt, (DateTime?)null),
                cancellationToken);

        return affected == 1;
    }

    public async Task<bool> TryUpdateAsync(Charge charge, CancellationToken cancellationToken = default)
    {
        db.Charges.Update(charge);

        // El token se compara contra el valor original. La entidad puede venir del tracker con el
        // ClaimedAt anterior al reclamo (que se hizo con ExecuteUpdate), así que se fija a mano:
        // "guardar solo si claimed_at sigue siendo el que escribió este pedido".
        db.Entry(charge).Property(c => c.ClaimedAt).OriginalValue = charge.ClaimedAt;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        // Se traduce a bool por lo mismo que en TryAddAsync: Business no depende de las excepciones de EF.
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(charge).State = EntityState.Detached;
            return false;
        }
    }
}
