using Booking.Business.Payments;

namespace Booking.Tests.TestDoubles;

public sealed class FakePaymentChargeClient : IPaymentChargeClient
{
    private readonly Queue<Func<PaymentChargeRequest, CancellationToken, Task<PaymentChargeResult>>> steps = new();
    private readonly List<PaymentChargeRequest> requests = [];
    private readonly object gate = new();

    public IReadOnlyList<PaymentChargeRequest> Requests => requests;

    public void EnqueueResult(PaymentChargeResult result) =>
        steps.Enqueue((_, _) => Task.FromResult(result));

    public void EnqueueFailure(PaymentChargeFailure failure) =>
        steps.Enqueue((_, _) => Task.FromException<PaymentChargeResult>(new PaymentChargeException(failure)));

    public void EnqueueStep(Func<PaymentChargeRequest, CancellationToken, Task<PaymentChargeResult>> step) =>
        steps.Enqueue(step);

    public Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Func<PaymentChargeRequest, CancellationToken, Task<PaymentChargeResult>> step;
        lock (gate)
        {
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("No payment charge result was configured for this call.");
            }

            requests.Add(request);
            step = steps.Dequeue();
        }

        return step(request, cancellationToken);
    }
}
