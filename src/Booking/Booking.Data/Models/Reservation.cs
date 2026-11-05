namespace Booking.Data.Models;

// Modelo anémico a propósito: la reserva solo transporta datos y no tiene métodos de transición.
// Los cambios de estado son UPDATE condicionales en ReservationRepository, para que sean atómicos
// aunque haya varias réplicas de Booking.
public class Reservation
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public Guid SeatId { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Retained;

    public ReservationRejectionReason? RejectionReason { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
