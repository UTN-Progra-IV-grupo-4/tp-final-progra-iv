using Payment.Data.Entities;
using Payment.Data.Repositories;

namespace Payment.Tests.TestDoubles;

// Repositorio que falla siempre con la excepción dada, para probar cómo la presentación la traduce
// sin tener que provocar la falla real en la base.
public sealed class FailingChargeRepository(Exception exception) : IChargeRepository
{
    public Task<Charge?> GetByReservationIdAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        Task.FromException<Charge?>(exception);

    public Task<bool> TryAddAsync(Charge charge, CancellationToken cancellationToken = default) =>
        Task.FromException<bool>(exception);

    public Task<bool> TryReclaimAsync(
        Guid chargeId, DateTime staleBefore, DateTime claimedAt, CancellationToken cancellationToken = default) =>
        Task.FromException<bool>(exception);

    public Task<bool> TryUpdateAsync(Charge charge, CancellationToken cancellationToken = default) =>
        Task.FromException<bool>(exception);
}
