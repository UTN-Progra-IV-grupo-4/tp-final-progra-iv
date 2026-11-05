using Booking.Data.Models;
using Booking.Data.Repositories;
using Microsoft.Extensions.Options;

namespace Booking.Business.Payments;

public enum ReservationPaymentOutcome
{
    Confirmed,
    PaymentFailed,
    PaymentUnavailable,
    Expired,
    LateApproved,
    AlreadyTerminal,
    NotFound
}

public sealed record ReservationPaymentResult(
    ReservationPaymentOutcome Outcome,
    Guid ReservationId,
    ReservationStatus? CurrentStatus = null,
    string? ChargeId = null,
    bool PaymentOutcomeUnknown = false);

// Cobra una reserva Retenida contra Payment y persiste el resultado con transiciones condicionales.
public sealed class ReservationPaymentProcessor
{
    private readonly IReservationRepository repository;
    private readonly IPaymentChargeClient chargeClient;
    private readonly TimeProvider timeProvider;
    private readonly PaymentProcessingOptions options;

    public ReservationPaymentProcessor(
        IReservationRepository repository,
        IPaymentChargeClient chargeClient,
        IOptions<PaymentProcessingOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(chargeClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.repository = repository;
        this.chargeClient = chargeClient;
        this.timeProvider = timeProvider;
        this.options = options.Value;
    }

    public async Task<ReservationPaymentResult> ProcessAsync(
        Guid reservationId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Reservation id is required.", nameof(reservationId));
        }

        // outcomeUnknown: algún intento terminó sin respuesta (Payment no disponible o plazo vencido),
        // así que el cobro pudo haberse realizado igual. Se arrastra hasta el resultado final para
        // advertir un posible cobro tardío.
        var outcomeUnknown = false;
        // Solo se reintentan fallos transitorios (Unavailable, DeadlineExceeded, Pending), hasta
        // MaxAttempts. Antes de cada intento se revalida que la reserva siga Retenida y sin vencer.
        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            var ineligible = await CheckEligibilityAsync(reservationId, cancellationToken);
            if (ineligible is not null)
            {
                return ineligible with { PaymentOutcomeUnknown = outcomeUnknown };
            }

            try
            {
                var charge = await chargeClient.ChargeAsync(
                    new PaymentChargeRequest(
                        reservationId,
                        options.AmountCents,
                        options.Currency,
                        correlationId),
                    cancellationToken);

                return await PersistChargeAsync(reservationId, charge, cancellationToken);
            }
            catch (PaymentChargeException exception) when (exception.Failure is
                PaymentChargeFailure.Unavailable or
                PaymentChargeFailure.DeadlineExceeded or
                PaymentChargeFailure.Pending)
            {
                // Pending no marca desconocido: Payment respondió que el cobro está en curso.
                outcomeUnknown |= exception.Failure is
                    PaymentChargeFailure.Unavailable or PaymentChargeFailure.DeadlineExceeded;
                if (attempt == options.MaxAttempts)
                {
                    if (exception.Failure == PaymentChargeFailure.Pending)
                    {
                        // El cobro sigue en curso en Payment: no se rechaza la reserva a ciegas, se
                        // propaga el fallo para que la capa Api derive el mensaje a revisión manual.
                        throw WithOutcomeUnknown(exception, outcomeUnknown);
                    }

                    var ineligibleAfterFailure = await CheckEligibilityAsync(reservationId, cancellationToken);
                    if (ineligibleAfterFailure is not null)
                    {
                        return ineligibleAfterFailure with { PaymentOutcomeUnknown = outcomeUnknown };
                    }

                    // Intentos agotados sin respuesta: se rechaza por PaymentUnavailable, pero se
                    // marca el resultado como desconocido porque podría existir un cobro tardío.
                    var result = await PersistOutcomeAsync(
                        reservationId,
                        ReservationStatus.Rejected,
                        ReservationRejectionReason.PaymentUnavailable,
                        ReservationPaymentOutcome.PaymentUnavailable,
                        cancellationToken);
                    return result with { PaymentOutcomeUnknown = true };
                }

                if (options.RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.RetryDelay, timeProvider, cancellationToken);
                }
            }
            catch (PaymentChargeException exception) when (outcomeUnknown && !exception.PaymentOutcomeUnknown)
            {
                // Non-retryable failure after an earlier ambiguous attempt: keep the "possible late charge" signal.
                throw WithOutcomeUnknown(exception, outcomeUnknown);
            }
        }

        throw new InvalidOperationException("Payment attempts ended without a result.");
    }

    private static PaymentChargeException WithOutcomeUnknown(
        PaymentChargeException exception,
        bool outcomeUnknown) =>
        !outcomeUnknown || exception.PaymentOutcomeUnknown
            ? exception
            : new PaymentChargeException(exception.Failure, exception) { PaymentOutcomeUnknown = true };

    private async Task<ReservationPaymentResult?> CheckEligibilityAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await repository.GetByIdAsync(reservationId, cancellationToken);
        if (reservation is null)
        {
            return new ReservationPaymentResult(ReservationPaymentOutcome.NotFound, reservationId);
        }

        if (reservation.Status != ReservationStatus.Retained)
        {
            return new ReservationPaymentResult(
                reservation.Status == ReservationStatus.Expired
                    ? ReservationPaymentOutcome.Expired
                    : ReservationPaymentOutcome.AlreadyTerminal,
                reservationId,
                reservation.Status);
        }

        if (reservation.ExpiresAt is null)
        {
            throw new InvalidOperationException("A retained reservation requires ExpiresAt.");
        }

        if (reservation.ExpiresAt > timeProvider.GetUtcNow())
        {
            return null;
        }

        await repository.TryExpireAsync(reservationId, timeProvider.GetUtcNow(), cancellationToken);
        return await ReadTerminalResultAsync(reservationId, false, cancellationToken);
    }

    private Task<ReservationPaymentResult> PersistChargeAsync(
        Guid reservationId,
        PaymentChargeResult charge,
        CancellationToken cancellationToken) => charge.Outcome switch
    {
        PaymentChargeOutcome.Approved => PersistOutcomeAsync(
            reservationId,
            ReservationStatus.Confirmed,
            null,
            ReservationPaymentOutcome.Confirmed,
            cancellationToken,
            approvedCharge: true,
            chargeId: charge.ChargeId),
        PaymentChargeOutcome.Declined => PersistOutcomeAsync(
            reservationId,
            ReservationStatus.Rejected,
            ReservationRejectionReason.PaymentFailed,
            ReservationPaymentOutcome.PaymentFailed,
            cancellationToken,
            chargeId: charge.ChargeId),
        _ => throw new PaymentChargeException(PaymentChargeFailure.UnexpectedResponse)
    };

    private async Task<ReservationPaymentResult> PersistOutcomeAsync(
        Guid reservationId,
        ReservationStatus status,
        ReservationRejectionReason? rejectionReason,
        ReservationPaymentOutcome outcome,
        CancellationToken cancellationToken,
        bool approvedCharge = false,
        string? chargeId = null)
    {
        var now = timeProvider.GetUtcNow();
        if (await repository.TrySetPaymentOutcomeAsync(
            reservationId, status, rejectionReason, now, cancellationToken))
        {
            return new ReservationPaymentResult(outcome, reservationId, status, chargeId);
        }

        await repository.TryExpireAsync(reservationId, timeProvider.GetUtcNow(), cancellationToken);
        return await ReadTerminalResultAsync(
            reservationId, approvedCharge, cancellationToken, chargeId);
    }

    private async Task<ReservationPaymentResult> ReadTerminalResultAsync(
        Guid reservationId,
        bool approvedCharge,
        CancellationToken cancellationToken,
        string? chargeId = null)
    {
        var current = await repository.GetByIdAsync(reservationId, cancellationToken) ??
            throw new InvalidOperationException("Reservation disappeared during payment processing.");

        if (current.Status == ReservationStatus.Retained)
        {
            throw new InvalidOperationException("A payment transition did not reach a terminal state.");
        }

        // LateApproved: Payment aprobó el cobro pero la reserva ya no estaba Retenida (venció o fue
        // rechazada antes), así que el cliente pagó sin quedarse con el asiento.
        var outcome = approvedCharge && current.Status != ReservationStatus.Confirmed
            ? ReservationPaymentOutcome.LateApproved
            : current.Status == ReservationStatus.Expired
                ? ReservationPaymentOutcome.Expired
                : ReservationPaymentOutcome.AlreadyTerminal;

        return new ReservationPaymentResult(outcome, reservationId, current.Status, chargeId);
    }
}
