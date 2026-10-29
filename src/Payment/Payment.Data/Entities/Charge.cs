namespace Payment.Data.Entities;

// Entidad anémica: solo datos. Las reglas (cuándo se aprueba, idempotencia) viven en Payment.Business.
// Se llama Charge y no Payment porque Payment ya es el namespace raíz del servicio (error CS0118).
public class Charge
{
    public Guid Id { get; set; }

    // Clave de idempotencia: un único cobro por reserva, garantizado con un índice único en la base.
    public Guid ReservationId { get; set; }

    public ChargeStatus Status { get; set; }

    // Null salvo que Status sea Declined.
    public string? FailureReason { get; set; }

    // Siempre en UTC: Npgsql mapea DateTime a timestamptz y rechaza fechas con otro Kind.
    public DateTime CreatedAt { get; set; }

    // Null mientras el cobro está Pending; sirve para auditar cuánto tardó la pasarela.
    public DateTime? CompletedAt { get; set; }
}
