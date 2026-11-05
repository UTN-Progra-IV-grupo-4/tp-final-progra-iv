namespace Booking.Business.Payments;

// Booking.Business depends on payment outcomes, not on gRPC messages or status codes.
public interface IPaymentChargeClient
{
    Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentChargeRequest(
    Guid ReservationId,
    long AmountCents,
    string Currency,
    string? CorrelationId);

public enum PaymentChargeOutcome
{
    Approved,
    Declined
}

public sealed record PaymentChargeResult(
    PaymentChargeOutcome Outcome,
    string? ChargeId);

public enum PaymentChargeFailure
{
    Unavailable,
    DeadlineExceeded,
    Pending,
    InvalidRequest,
    UnexpectedResponse
}

public sealed class PaymentChargeException(
    PaymentChargeFailure failure,
    Exception? innerException = null)
    : Exception($"Payment charge failed: {failure}.", innerException)
{
    public PaymentChargeFailure Failure { get; } = failure;

    // Set by Business when an earlier attempt ended without a known result, so a charge may exist.
    public bool PaymentOutcomeUnknown { get; init; }
}
