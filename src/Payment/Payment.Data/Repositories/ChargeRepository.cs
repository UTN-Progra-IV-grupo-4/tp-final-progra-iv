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

    public async Task<bool> TryReclaimFailedAsync(Guid chargeId, CancellationToken cancellationToken = default)
    {
        // UPDATE condicional en un solo paso: la base evalúa el WHERE con la fila bloqueada, así que
        // entre dos reintentos simultáneos solo uno ve Failed. Leer y después guardar dejaría a ambos cobrar.
        // Va directo a la base sin pasar por el change tracker: la entidad en memoria sigue diciendo Failed.
        var affected = await db.Charges
            .Where(c => c.Id == chargeId && c.Status == ChargeStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ChargeStatus.Pending)
                .SetProperty(c => c.FailureReason, (string?)null)
                .SetProperty(c => c.CompletedAt, (DateTime?)null),
                cancellationToken);

        return affected == 1;
    }

    public async Task UpdateAsync(Charge charge, CancellationToken cancellationToken = default)
    {
        db.Charges.Update(charge);
        await db.SaveChangesAsync(cancellationToken);
    }
}
