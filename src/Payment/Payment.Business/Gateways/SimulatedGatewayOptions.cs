namespace Payment.Business.Gateways;

// Las demoras dependen del deadline gRPC de Booking y de la retención de #17: en configuración
// se ajustan sin recompilar cuando esos valores cambien.
public class SimulatedGatewayOptions
{
    public const string SectionName = "SimulatedGateway";

    // Mayor que el deadline de Booking: provoca el timeout y prueba el reintento idempotente.
    public int SlowDelaySeconds { get; set; } = 15;

    // Mayor que la retención: el resultado llega tarde y la reserva vence.
    public int ExpirationDelaySeconds { get; set; } = 120;
}
