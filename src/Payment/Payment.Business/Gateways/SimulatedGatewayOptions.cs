using System.ComponentModel.DataAnnotations;

namespace Payment.Business.Gateways;

public enum SimulatedMode { Approve, Decline, Slow, Error }

// Las demoras dependen del deadline gRPC de Booking y de la retención de las reservas de Booking: en configuración
// se ajustan sin recompilar cuando esos valores cambien.
// Se lee al arrancar: para cambiar de caso, SimulatedGateway__Mode=Decline y reiniciar solo Payment.
public class SimulatedGatewayOptions
{
    public const string SectionName = "SimulatedGateway";

    // Qué responde la pasarela simulada en cada llamada.
    [Required]
    public SimulatedMode Mode { get; set; } = SimulatedMode.Approve;

    // Mayor que el deadline de Booking: provoca el timeout y prueba el reintento idempotente.
    // Si además supera la retención, el resultado llega tarde y la reserva vence.
    [Range(1, 300)]
    public int SlowDelaySeconds { get; set; } = 15;
}
