using Microsoft.Extensions.Logging;
using Payment.Business.Gateways;
using Payment.Data.Entities;
using Payment.Data.Repositories;

namespace Payment.Business;

// Caso de uso: cobrar una reserva retenida. Es idempotente por ReservationId: un reintento devuelve
// el cobro guardado y solo vuelve a cobrar si el intento anterior falló técnicamente.
public class ChargeService(
    IChargeRepository charges,
    IPaymentGateway gateway,
    ILogger<ChargeService> logger)
{
    public async Task<ChargeResult> ChargeAsync(
        Guid reservationId,
        long amountCents,
        string currency,
        CancellationToken cancellationToken)
    {
        // Inicio de la cadena (punto f): también los reintentos quedan registrados.
        logger.LogInformation("Inicio de cobro. ReservationId={ReservationId}", reservationId);

        // 1. ¿Ya existe un cobro para esta reserva?
        var existing = await charges.GetByReservationIdAsync(reservationId, cancellationToken);
        if (existing is not null)
            return await ResolveExistingAsync(existing, amountCents, currency, cancellationToken);

        // 2. Reclamo: guardamos el cobro como Pending antes de llamar a la pasarela.
        //    El índice único sobre ReservationId decide quién gana si dos pedidos llegan juntos.
        var charge = new Charge
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            AmountCents = amountCents,
            Currency = currency,
            Status = ChargeStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        if (!await charges.TryAddAsync(charge, cancellationToken))
        {
            // Perdimos la carrera: otro pedido insertó primero. Lo tratamos como reintento.
            var winner = await charges.GetByReservationIdAsync(reservationId, cancellationToken)
                ?? throw new InvalidOperationException($"Reclamo de cobro inconsistente para {reservationId}.");
            return await ResolveExistingAsync(winner, amountCents, currency, cancellationToken);
        }

        return await ExecuteChargeAsync(charge, cancellationToken);
    }

    private async Task<ChargeResult> ResolveExistingAsync(
        Charge existing,
        long amountCents,
        string currency,
        CancellationToken cancellationToken)
    {
        // Antes que el estado: devolver el cobro guardado haría creer a Booking que cobró otro importe,
        // y reclamar un Failed cobraría uno distinto del registrado.
        if (existing.AmountCents != amountCents || !string.Equals(existing.Currency, currency, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Pedido distinto del cobro registrado. ReservationId={ReservationId} ChargeId={ChargeId} " +
                "Registrado={StoredAmountCents} {StoredCurrency} Pedido={RequestedAmountCents} {RequestedCurrency}",
                existing.ReservationId, existing.Id, existing.AmountCents, existing.Currency, amountCents, currency);
            throw new ChargeMismatchException(existing.ReservationId);
        }

        switch (existing.Status)
        {
            case ChargeStatus.Approved:
            case ChargeStatus.Declined:
                logger.LogInformation(
                    "Reintento resuelto con el cobro guardado. ReservationId={ReservationId} ChargeId={ChargeId}",
                    existing.ReservationId, existing.Id);
                return ToResult(existing, isReplay: true);

            case ChargeStatus.Failed:
                // La falla anterior fue técnica: reclamamos el cobro para intentarlo de nuevo.
                // Si otro pedido lo reclamó primero, no cobramos.
                if (!await charges.TryReclaimFailedAsync(existing.Id, cancellationToken))
                    throw new ChargeInProgressException(existing.ReservationId);

                existing.Status = ChargeStatus.Pending;
                logger.LogInformation(
                    "Reintento de un cobro fallido. ReservationId={ReservationId} ChargeId={ChargeId}",
                    existing.ReservationId, existing.Id);
                return await ExecuteChargeAsync(existing, cancellationToken);

            case ChargeStatus.Pending:
                // Otro pedido está cobrando en este momento: todavía no hay resultado que devolver.
                throw new ChargeInProgressException(existing.ReservationId);

            default:
                // Bug, no un cobro en curso: la capa gRPC lo responde INTERNAL para que no se reintente.
                throw new InvalidOperationException($"Estado de cobro inesperado: {existing.Status}.");
        }
    }

    // Precondición: el cobro ya está reclamado en estado Pending por este pedido.
    private async Task<ChargeResult> ExecuteChargeAsync(Charge charge, CancellationToken cancellationToken)
    {
        GatewayResult gatewayResult;
        try
        {
            gatewayResult = await gateway.ChargeAsync(charge.AmountCents, charge.Currency, cancellationToken);
        }
        catch (Exception ex)
        {
            // Falla técnica: dejamos el registro como Failed para auditoría y para un futuro reintento.
            // CancellationToken.None: el registro tiene que guardarse aunque Booking ya haya cortado.
            charge.Status = ChargeStatus.Failed;
            charge.CompletedAt = DateTime.UtcNow;
            await charges.UpdateAsync(charge, CancellationToken.None);

            logger.LogWarning(ex, "Falla técnica al cobrar. ReservationId={ReservationId} ChargeId={ChargeId}",
                charge.ReservationId, charge.Id);
            throw;
        }

        // La pasarela ya respondió: el resultado se guarda sin cancelación para no perderlo.
        charge.Status = gatewayResult.IsApproved ? ChargeStatus.Approved : ChargeStatus.Declined;
        charge.FailureReason = gatewayResult.DeclineReason;
        charge.CompletedAt = DateTime.UtcNow;
        await charges.UpdateAsync(charge, CancellationToken.None);

        // Fin de la cadena (punto f).
        logger.LogInformation(
            "Fin de cobro. ReservationId={ReservationId} ChargeId={ChargeId} Estado={Status}",
            charge.ReservationId, charge.Id, charge.Status);

        return ToResult(charge, isReplay: false);
    }

    private static ChargeResult ToResult(Charge charge, bool isReplay) => new(
        ReservationId: charge.ReservationId,
        ChargeId: charge.Id,
        Outcome: charge.Status == ChargeStatus.Approved ? ChargeOutcome.Approved : ChargeOutcome.Declined,
        DeclineReason: charge.FailureReason,
        IsReplay: isReplay);
}
