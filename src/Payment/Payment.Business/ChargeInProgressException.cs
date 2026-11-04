namespace Payment.Business;

// Otro pedido tiene reclamado el cobro (Pending) y todavía no hay resultado que devolver. Excepción
// propia y no InvalidOperationException: la capa gRPC la traduce a ABORTED, que Booking reintenta,
// y una inconsistencia interna no debe leerse como "esperá y reintentá".
public class ChargeInProgressException(Guid reservationId)
    : Exception($"Ya hay un cobro en curso para la reserva {reservationId}.")
{
    public Guid ReservationId { get; } = reservationId;
}
