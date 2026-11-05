using Payment.Business.Gateways;

namespace Payment.Tests.TestDoubles;

// Pasarela controlable: cuenta las llamadas (la prueba de que no se cobró dos veces) y puede retener
// o hacer fallar solo la próxima, para armar un cobro en curso sin depender de demoras reales.
public sealed class FakePaymentGateway : IPaymentGateway
{
    private int calls;
    private TaskCompletionSource? holdNext;
    private Exception? failNext;

    public int Calls => Volatile.Read(ref calls);

    // Se completa cuando alguna llamada entra a la pasarela: con eso el test sabe que el cobro ya está Pending.
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // La próxima llamada espera hasta que el test complete el TaskCompletionSource devuelto.
    public TaskCompletionSource HoldNextCall()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        holdNext = gate;
        return gate;
    }

    // La próxima llamada lanza la excepción, como una falla técnica de la pasarela.
    public void FailNextCall(Exception exception) => failNext = exception;

    public async Task<GatewayResult> ChargeAsync(long amountCents, string currency, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref calls);
        var gate = Interlocked.Exchange(ref holdNext, null);
        var failure = Interlocked.Exchange(ref failNext, null);
        Entered.TrySetResult();

        if (gate is not null)
            await gate.Task;

        if (failure is not null)
            throw failure;

        return new GatewayResult(IsApproved: true, DeclineReason: null);
    }
}
