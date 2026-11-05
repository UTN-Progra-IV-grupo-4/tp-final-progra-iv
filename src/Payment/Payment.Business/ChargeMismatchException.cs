namespace Payment.Business;

// Mismo reservation_id con otro monto o moneda. Excepción propia y no ArgumentException: la capa gRPC
// la traduce a INVALID_ARGUMENT, y una ArgumentException interna de una librería no debe leerse así.
public class ChargeMismatchException(Guid reservationId)
    : Exception($"La reserva {reservationId} ya tiene un cobro con otro monto o moneda.")
{
    public Guid ReservationId { get; } = reservationId;
}
