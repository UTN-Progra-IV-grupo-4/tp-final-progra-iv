using Microsoft.Extensions.Options;

namespace Payment.Business.Gateways;

// No hay pasarela real: el resultado lo elige SimulatedGateway:Mode y no el monto, así el precio
// que configura Booking queda limpio y cada caso de la demo se fuerza reiniciando solo Payment.
public class SimulatedPaymentGateway(IOptions<SimulatedGatewayOptions> options) : IPaymentGateway
{
    private readonly SimulatedGatewayOptions _options = options.Value;

    public async Task<GatewayResult> ChargeAsync(long amountCents, string currency, CancellationToken cancellationToken = default)
    {
        switch (_options.Mode)
        {
            case SimulatedMode.Approve:
                return new GatewayResult(IsApproved: true, DeclineReason: null);

            case SimulatedMode.Decline:
                return new GatewayResult(IsApproved: false, DeclineReason: "Fondos insuficientes");

            case SimulatedMode.Slow:
                await DelayIgnoringCancellationAsync();
                return new GatewayResult(IsApproved: true, DeclineReason: null);

            case SimulatedMode.Error:
                // HttpRequestException imita la caída de una pasarela real, que responde por HTTP.
                throw new HttpRequestException("Error simulado de la pasarela de pago.");

            default:
                // La capa gRPC la responde INTERNAL: reintentar no arregla la configuración.
                // Un modo desconocido es un error de configuración y nunca debe terminar en un aprobado.
                throw new NotSupportedException($"Modo simulado desconocido: {_options.Mode}.");
        }
    }

    // Una pasarela real no deshace un cargo porque el cliente cortó la conexión: si el deadline de
    // Booking vence, el cobro sigue Pending, los reintentos reciben ABORTED y el resultado se guarda
    // igual al terminar. Por eso esta espera no recibe el token de la llamada gRPC.
    private Task DelayIgnoringCancellationAsync() =>
        Task.Delay(TimeSpan.FromSeconds(_options.SlowDelaySeconds), CancellationToken.None);
}
