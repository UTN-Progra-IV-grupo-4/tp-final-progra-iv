using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Business.Gateways;
using Payment.Data.Entities;
using Payment.Data.Repositories;

namespace Payment.Business;

// Caso de uso: cobrar una reserva retenida. Es idempotente por ReservationId: un reintento devuelve
// el cobro guardado y solo vuelve a cobrar si el intento anterior falló técnicamente o quedó abandonado.
// TimeProvider y no DateTime.UtcNow: los tests pueden adelantar el reloj para probar el vencimiento.
public class ChargeService(
    IChargeRepository charges,
    IPaymentGateway gateway,
    IOptions<ChargeOptions> options,
    TimeProvider timeProvider,
    ILogger<ChargeService> logger)
{
    public async Task<ChargeResult> ChargeAsync(
        Guid reservationId,
        long amountCents,
        string currency,
        CancellationToken cancellationToken)
    {
        // Inicio de la cadena (punto f): también los reintentos quedan registrados.
        logger.LogInformation(
            PaymentAuditEvents.ChargeStarted, "Inicio de cobro. ReservationId={ReservationId}", reservationId);

        // 1. ¿Ya existe un cobro para esta reserva?
        var existing = await charges.GetByReservationIdAsync(reservationId, cancellationToken);
        if (existing is not null)
            return await ResolveExistingAsync(existing, amountCents, currency, cancellationToken);

        // 2. Reclamo: guardamos el cobro como Pending antes de llamar a la pasarela.
        //    El índice único sobre ReservationId decide quién gana si dos pedidos llegan juntos.
        var now = UtcNow();
        var charge = new Charge
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            AmountCents = amountCents,
            Currency = currency,
            Status = ChargeStatus.Pending,
            CreatedAt = now,
            ClaimedAt = now
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
                    PaymentAuditEvents.ChargeReplayed,
                    "Reintento resuelto con el cobro guardado. ReservationId={ReservationId} ChargeId={ChargeId} Estado={Status}",
                    existing.ReservationId, existing.Id, existing.Status);
                return ToResult(existing, isReplay: true);

            case ChargeStatus.Failed:
                // La falla anterior fue técnica: reclamamos el cobro para intentarlo de nuevo.
                return await ReclaimAndChargeAsync(existing, cancellationToken);

            case ChargeStatus.Pending:
                // Reclamado hace más que PendingTimeout: el proceso que lo tenía se cayó y nadie va a
                // terminarlo. Se chequea acá, con el mismo "<" que el UPDATE, para no ir a la base en el
                // caso común (un cobro en curso); el UPDATE condicional sigue decidiendo si dos compiten.
                if (existing.ClaimedAt < UtcNow() - options.Value.PendingTimeout)
                {
                    return await ReclaimAndChargeAsync(existing, cancellationToken);
                }

                // Otro pedido está cobrando en este momento: todavía no hay resultado que devolver.
                throw new ChargeInProgressException(existing.ReservationId);

            default:
                // Bug, no un cobro en curso: la capa gRPC lo responde INTERNAL para que no se reintente.
                throw new InvalidOperationException($"Estado de cobro inesperado: {existing.Status}.");
        }
    }

    // Toma el turno sobre un cobro existente (Failed, o Pending vencido) y lo cobra. Si otro pedido
    // lo reclamó primero, no cobramos: ese pedido es el que está cobrando ahora.
    private async Task<ChargeResult> ReclaimAndChargeAsync(Charge existing, CancellationToken cancellationToken)
    {
        var previousStatus = existing.Status;
        var previousClaimedAt = existing.ClaimedAt;
        var now = UtcNow();
        var staleBefore = now - options.Value.PendingTimeout;

        if (!await charges.TryReclaimAsync(existing.Id, staleBefore, now, cancellationToken))
            throw new ChargeInProgressException(existing.ReservationId);

        // El reclamo se hizo directo en la base: la entidad en memoria se alinea con lo que quedó guardado.
        existing.Status = ChargeStatus.Pending;
        existing.ClaimedAt = now;
        existing.FailureReason = null;
        existing.CompletedAt = null;

        // Warning solo para un Pending vencido y recién cuando se ganó el reclamo: con una pasarela real, el
        // proceso caído pudo haber cobrado sin registrarlo. Se loguea el ClaimedAt original porque el reclamo
        // lo sobrescribe.
        if (previousStatus == ChargeStatus.Pending)
        {
            logger.LogWarning(
                "Cobro Pending abandonado, reclamado. ReservationId={ReservationId} ChargeId={ChargeId} " +
                "ReclamadoEn={ClaimedAt:O}",
                existing.ReservationId, existing.Id, previousClaimedAt);
        }

        logger.LogInformation(
            "Cobro reclamado para reintentar. EstadoAnterior={PreviousStatus} ReservationId={ReservationId} ChargeId={ChargeId}",
            previousStatus, existing.ReservationId, existing.Id);
        return await ExecuteChargeAsync(existing, cancellationToken);
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
            charge.CompletedAt = UtcNow();
            if (!await charges.TryUpdateAsync(charge, CancellationToken.None))
                LogLostClaim(charge);

            logger.LogWarning(ex, "Falla técnica al cobrar. ReservationId={ReservationId} ChargeId={ChargeId}",
                charge.ReservationId, charge.Id);
            throw;
        }

        // La pasarela ya respondió: el resultado se guarda sin cancelación para no perderlo.
        charge.Status = gatewayResult.IsApproved ? ChargeStatus.Approved : ChargeStatus.Declined;
        charge.FailureReason = gatewayResult.DeclineReason;
        charge.CompletedAt = UtcNow();
        if (!await charges.TryUpdateAsync(charge, CancellationToken.None))
        {
            // Este pedido tardó más que PendingTimeout y otro lo reclamó: el resultado vigente es el del otro.
            LogLostClaim(charge);
            throw new ChargeInProgressException(charge.ReservationId);
        }

        // Fin de la cadena (punto f).
        logger.LogInformation(
            PaymentAuditEvents.ChargeFinished,
            "Fin de cobro. ReservationId={ReservationId} ChargeId={ChargeId} Estado={Status}",
            charge.ReservationId, charge.Id, charge.Status);

        return ToResult(charge, isReplay: false);
    }

    // Warning y no Information: con una pasarela real, la respuesta descartada puede ser un cargo hecho.
    private void LogLostClaim(Charge charge) =>
        logger.LogWarning(
            "Resultado descartado: otro pedido reclamó el cobro vencido. ReservationId={ReservationId} " +
            "ChargeId={ChargeId} EstadoDescartado={Status}",
            charge.ReservationId, charge.Id, charge.Status);

    // PostgreSQL guarda microsegundos y .NET usa ticks de 100 ns: se trunca para que el ClaimedAt en
    // memoria sea exactamente el guardado, porque el token de concurrencia lo compara por igualdad.
    private DateTime UtcNow()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static ChargeResult ToResult(Charge charge, bool isReplay) => new(
        ReservationId: charge.ReservationId,
        ChargeId: charge.Id,
        Outcome: charge.Status == ChargeStatus.Approved ? ChargeOutcome.Approved : ChargeOutcome.Declined,
        DeclineReason: charge.FailureReason,
        IsReplay: isReplay);
}
