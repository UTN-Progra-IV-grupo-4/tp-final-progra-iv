using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

internal static class ReservationRequestMappingErrorCodes
{
    public const string InvalidJson = "InvalidJson";
    public const string RequestIdRequired = "RequestIdRequired";
    public const string EventIdRequired = "EventIdRequired";
    public const string SeatIdRequired = "SeatIdRequired";
    public const string MessageIdRequired = "MessageIdRequired";
    public const string MessageIdInvalid = "MessageIdInvalid";
    public const string MessageIdMismatch = "MessageIdMismatch";
}

internal sealed record ReservationRequestMappingResult(
    ProcessReservationRequest? Request,
    string? ErrorCode)
{
    public bool IsValid => Request is not null && ErrorCode is null;

    public static ReservationRequestMappingResult Success(ProcessReservationRequest request) =>
        new(request, null);

    public static ReservationRequestMappingResult Invalid(string errorCode) =>
        new(null, errorCode);
}
