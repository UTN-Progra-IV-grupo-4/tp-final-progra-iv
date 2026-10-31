using Microsoft.Extensions.Options;

namespace Payment.Business.Gateways;

// No hay pasarela real: los centavos del monto eligen el comportamiento, como las tarjetas de prueba
// de las pasarelas reales. El precio fijo lo configura Booking (Payment:Amount), así que para forzar
// un caso en la demo alcanza con cambiarlo allá.
public class SimulatedPaymentGateway(IOptions<SimulatedGatewayOptions> options) : IPaymentGateway
{
    private readonly SimulatedGatewayOptions _options = options.Value;

    public async Task<GatewayResult> ChargeAsync(long amountCents, string currency, CancellationToken cancellationToken = default)
    {
        // TODO(human): implementar la regla según amountCents % 100
        //   01 → rechazo "fondos insuficientes"     02 → rechazo "tarjeta bloqueada"
        //   03 → demora SlowDelaySeconds y aprueba  04 → error técnico (excepción)
        //   05 → demora ExpirationDelaySeconds y aprueba
        //   otro → aprueba
        throw new NotImplementedException();
    }
}
