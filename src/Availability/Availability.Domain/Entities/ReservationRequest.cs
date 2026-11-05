namespace Availability.Domain.Entities;

// Una solicitud recibida no es una reserva confirmada: Booking decide su resultado.
// Entidad anémica: solo transporta datos; registrar y publicar la solicitud es trabajo de Application.
public class ReservationRequest
{
    public Guid Id { get; set; }
    public Guid SeatId { get; set; }
    // El evento se obtiene de Seat.EventId: no duplicamos un dato que podría contradecirse.
    // Identificador externo del usuario (claim sub de OIDC), no una FK a otra base.
    public string UserId { get; set; } = string.Empty;
    // Application debe asignar UTC (offset cero) para que el mensaje compartido lleve un DateTime UTC.
    public DateTimeOffset RequestedAt { get; set; }
    public Seat Seat { get; set; } = null!;
}
