namespace Availability.Domain.Entities;

// Una solicitud recibida no es una reserva confirmada: Booking decide su resultado.
public class ReservationRequest
{
    public Guid Id { get; set; }
    public Guid SeatId { get; set; }
    // Identificador externo del usuario (claim sub de OIDC), no una FK a otra base.
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public Seat Seat { get; set; } = null!;
}
