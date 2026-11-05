namespace Booking.Data.Models;

// Motivo por el que una reserva quedó en estado Rejected.
public enum ReservationRejectionReason
{
    // Otra reserva retenida o confirmada ya ocupa el asiento.
    SeatTaken,
    // Payment rechazó el cobro de forma definitiva.
    PaymentFailed,
    // No se obtuvo un resultado de Payment tras agotar los reintentos.
    PaymentUnavailable
}
