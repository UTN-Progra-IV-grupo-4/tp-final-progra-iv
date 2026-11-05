namespace Booking.Business.Dtos;

public class ReservationStatusResponse
{
    public required Guid RequestId { get; init; }

    public required string Status { get; init; }

    public string? RejectionReason { get; init; }
}
