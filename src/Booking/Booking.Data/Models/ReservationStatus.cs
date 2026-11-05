namespace Booking.Data.Models;

// Ciclo de vida de una reserva. Las transiciones son UPDATE condicionales en el repositorio, no
// métodos de la entidad.
public enum ReservationStatus
{
    // Retenida: el asiento queda reservado temporalmente hasta ExpiresAt, mientras se espera el pago.
    Retained,
    // Confirmada: el pago fue aprobado dentro del plazo de retención; el asiento queda ocupado.
    Confirmed,
    // Rechazada: no se pudo reservar o cobrar (ver ReservationRejectionReason); no ocupa el asiento.
    Rejected,
    // Vencida: la retención expiró sin pago aprobado; el asiento vuelve a estar disponible.
    Expired
}
