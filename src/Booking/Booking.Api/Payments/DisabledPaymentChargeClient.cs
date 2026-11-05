using Booking.Business.Payments;

namespace Booking.Api.Payments;

// Registered only while Payment:Enabled is false. The consumer never reaches the payment step in
// that mode; this client exists so the dependency graph is complete (Development validates every
// registration at startup) and it fails loudly if something ever does call it.
internal sealed class DisabledPaymentChargeClient : IPaymentChargeClient
{
    public Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Payment is disabled. Set Payment:Enabled to true and configure Payment:Address to charge reservations.");
}
