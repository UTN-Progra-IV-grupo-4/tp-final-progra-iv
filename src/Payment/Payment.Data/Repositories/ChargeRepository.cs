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

    public async Task UpdateAsync(Charge charge, CancellationToken cancellationToken = default)
    {
        db.Charges.Update(charge);
        await db.SaveChangesAsync(cancellationToken);
    }
}
