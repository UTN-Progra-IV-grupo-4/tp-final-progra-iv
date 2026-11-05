namespace Availability.Application.Abstractions;
public enum ReservationStatus
{
    Retained,
    Confirmed,
    Rejected,
    Expired
}
public enum ReservationRejectionReason
{
    SeatTaken,
    PaymentFailed,
    PaymentUnavailable
}
public class ReservationStatusResult
{
    public Guid RequestId { get; set; }
    public ReservationStatus Status { get; set; }
    public ReservationRejectionReason? RejectionReason { get; set; }
}