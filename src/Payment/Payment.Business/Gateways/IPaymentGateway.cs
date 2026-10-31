namespace Payment.Business.Gateways;

// Adapter: el caso de uso habla en términos de negocio y no sabe si abajo hay una pasarela real
// o la simulada. Cambiar de proveedor es cambiar el registro en DI, no ChargeService.
public interface IPaymentGateway
{
    // Un rechazo (fondos, tarjeta) vuelve en el resultado; un error técnico se lanza como excepción.
    // Así ChargeService distingue Declined de Failed sin mirar mensajes.
    Task<GatewayResult> ChargeAsync(long amountCents, string currency, CancellationToken cancellationToken = default);
}

// Junto a la interfaz porque solo existe como su respuesta.
public sealed record GatewayResult(bool IsApproved, string? DeclineReason);
