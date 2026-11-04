namespace Booking.Business.Dtos;

// Define los datos de respuesta.
public class ReservationStatusResponse
{
    public required Guid RequestId { get; init; }

    public required string Status { get; init; }

    public string? RejectionReason { get; init; }
}
