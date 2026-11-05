namespace Booking.Business.Payments;

// Puerto definido en Business e implementado en Api (GrpcPaymentChargeClient): Business depende de
// los resultados del cobro, no de mensajes ni códigos de estado gRPC.
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

// Unavailable y DeadlineExceeded dejan el resultado del cobro desconocido (pudo haberse realizado);
// Pending significa que Payment tiene un cobro en curso para esa reserva. Las tres se reintentan.
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

    // Lo marca Business cuando un intento previo terminó sin resultado conocido, así que puede existir un cobro.
    public bool PaymentOutcomeUnknown { get; init; }
}
