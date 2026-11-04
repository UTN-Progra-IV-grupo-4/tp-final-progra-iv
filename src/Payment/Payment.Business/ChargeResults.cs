namespace Payment.Business;

public enum ChargeOutcome { Approved, Declined }

// Resultado del caso de uso "cobrar una reserva retenida".
// Un rechazo de negocio es un resultado normal. Un error técnico no entra acá: se lanza como excepción.
// record : Es inmutable y compara por valor, así que dos resultados con los mismos datos son iguales.
public sealed record ChargeResult(
    Guid ReservationId,
    Guid ChargeId,
    ChargeOutcome Outcome,
    string? DeclineReason,
    bool IsReplay);
