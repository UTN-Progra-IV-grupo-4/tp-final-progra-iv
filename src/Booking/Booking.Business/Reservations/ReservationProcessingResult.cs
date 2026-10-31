using Booking.Data.Models;

namespace Booking.Business.Reservations;

public enum ReservationProcessingOutcome
{
    Retained,
    Duplicate,
    SeatTaken,
    RequestConflict,
    InvalidRequest
}

public static class ReservationProcessingErrorCodes
{
    public const string RequestIdRequired = "RequestIdRequired";
    public const string EventIdRequired = "EventIdRequired";
    public const string SeatIdRequired = "SeatIdRequired";
    public const string RequestPayloadConflict = "RequestPayloadConflict";
}

public sealed record ReservationProcessingResult(
    ReservationProcessingOutcome Outcome,
    Guid RequestId,
    ReservationStatus? CurrentStatus = null,
    ReservationRejectionReason? RejectionReason = null,
    string? ErrorCode = null);
