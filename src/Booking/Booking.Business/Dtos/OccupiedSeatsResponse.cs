namespace Booking.Business.Dtos;

public class OccupiedSeatsResponse
{
    public required Guid EventId { get; init; }

    public required IReadOnlyList<Guid> SeatIds { get; init; }
}
