namespace Availability.Domain.Entities;

// Una solicitud recibida no es una reserva confirmada: Booking decide su resultado.
public class ReservationRequest
{
    public Guid Id { get; set; }
    public Guid SeatId { get; set; }
    // El evento se obtiene de Seat.EventId: no duplicamos un dato que podría contradecirse.
    // Identificador externo del usuario (claim sub de OIDC), no una FK a otra base.
    public string UserId { get; set; } = string.Empty;
    // Application debe asignar UTC (offset cero). El mapper del #15 deberá usar
    // RequestedAt.UtcDateTime para el DateTime UTC del mensaje compartido.
    public DateTimeOffset RequestedAt { get; set; }
    public Seat Seat { get; set; } = null!;
}
