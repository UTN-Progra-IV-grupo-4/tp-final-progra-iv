namespace Payment.Data.Entities;

// Entidad anémica: solo datos. Las reglas (cuándo se aprueba, idempotencia) viven en Payment.Business.
// Se llama Charge y no Payment porque Payment ya es el namespace raíz del servicio (error CS0118).
public class Charge
{
    public Guid Id { get; set; }

    // Clave de idempotencia: un único cobro por reserva, garantizado con un índice único en la base.
    public Guid ReservationId { get; set; }

    // Se guarda lo que se intentó cobrar para auditoría, aunque el precio lo defina Booking.
    // Centavos enteros, igual que en el contrato: decimal en la base no aporta nada si nunca hay fracciones.
    public long AmountCents { get; set; }

    // Código ISO 4217 (ej. "ARS").
    public string Currency { get; set; } = string.Empty;

    public ChargeStatus Status { get; set; }

    // Null salvo que Status sea Declined.
    public string? FailureReason { get; set; }

    // Siempre en UTC: Npgsql mapea DateTime a timestamptz y rechaza fechas con otro Kind.
    public DateTime CreatedAt { get; set; }

    // Cuándo tomó el cobro el último pedido que lo reclamó (al crearlo o al reintentarlo). Distinto de
    // CreatedAt, que no cambia: con él un Failed reclamado hace una hora se vería vencido apenas empieza.
    // También es token de concurrencia: un proceso solo guarda su resultado si el reclamo sigue siendo suyo.
    public DateTime ClaimedAt { get; set; }

    // Null mientras el cobro está Pending; sirve para auditar cuánto tardó la pasarela.
    public DateTime? CompletedAt { get; set; }
}
