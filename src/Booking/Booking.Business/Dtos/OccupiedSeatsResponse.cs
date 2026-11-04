namespace Booking.Business.Dtos;

// Define la respuesta de asientos ocupados por evento.
public class OccupiedSeatsResponse
{
    public required Guid EventId { get; init; }

    public required IReadOnlyList<Guid> SeatIds { get; init; }
}
