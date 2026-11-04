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

        var outcomeUnknown = false;
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
                outcomeUnknown |= exception.Failure is
                    PaymentChargeFailure.Unavailable or PaymentChargeFailure.DeadlineExceeded;
                if (attempt == options.MaxAttempts)
                {
                    if (exception.Failure == PaymentChargeFailure.Pending)
                    {
                        throw;
                    }

                    var ineligibleAfterFailure = await CheckEligibilityAsync(reservationId, cancellationToken);
                    if (ineligibleAfterFailure is not null)
                    {
                        return ineligibleAfterFailure with { PaymentOutcomeUnknown = outcomeUnknown };
                    }

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
        }

        throw new InvalidOperationException("Payment attempts ended without a result.");
    }

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

        var outcome = approvedCharge && current.Status != ReservationStatus.Confirmed
            ? ReservationPaymentOutcome.LateApproved
            : current.Status == ReservationStatus.Expired
                ? ReservationPaymentOutcome.Expired
                : ReservationPaymentOutcome.AlreadyTerminal;

        return new ReservationPaymentResult(outcome, reservationId, current.Status, chargeId);
    }
}
