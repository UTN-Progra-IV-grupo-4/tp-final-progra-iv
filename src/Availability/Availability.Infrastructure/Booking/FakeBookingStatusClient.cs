using Availability.Application.Abstractions;
namespace Availability.Infrastructure.Booking;
// Temporary stand-in for Booking until the real HTTP client against #21 exists.
// Picks a result from the request id so every case can be seen:
// processing (404), Retained, Confirmed, Rejected and Expired.
public class FakeBookingStatusClient : IBookingStatusClient
{
    public Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        ReservationStatusResult? result = (requestId.ToByteArray()[0] % 5) switch
        {
            0 => null,
            1 => new ReservationStatusResult { RequestId = requestId, Status = ReservationStatus.Retained },
            2 => new ReservationStatusResult { RequestId = requestId, Status = ReservationStatus.Confirmed },
            3 => new ReservationStatusResult
            {
                RequestId = requestId,
                Status = ReservationStatus.Rejected,
                RejectionReason = ReservationRejectionReason.SeatTaken
            },
            _ => new ReservationStatusResult { RequestId = requestId, Status = ReservationStatus.Expired }
        };
        return Task.FromResult(result);
    }
}