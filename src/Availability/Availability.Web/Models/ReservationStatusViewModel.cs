using Availability.Application.Abstractions;
namespace Availability.Web.Models;
// Data shown by the reservation status view.
// Status is null while Booking has not consumed the request yet ("processing").
public class ReservationStatusViewModel
{
    public Guid RequestId { get; set; }
    public ReservationStatus? Status { get; set; }
    public ReservationRejectionReason? RejectionReason { get; set; }
    public bool BookingUnavailable { get; set; }
    public int PollIntervalSeconds { get; set; }
    public int PollMaxSeconds { get; set; }
}   