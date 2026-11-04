using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

internal static class ReservationRequestMappingErrorCodes
{
    public const string InvalidJson = "InvalidJson";
    public const string PayloadMessageIdRequired = "PayloadMessageIdRequired";
    public const string EventIdRequired = "EventIdRequired";
    public const string SeatIdRequired = "SeatIdRequired";
    public const string MessageIdRequired = "MessageIdRequired";
    public const string MessageIdInvalid = "MessageIdInvalid";
    public const string MessageIdMismatch = "MessageIdMismatch";
    public const string CorrelationIdRequired = "CorrelationIdRequired";
    public const string CorrelationIdInvalid = "CorrelationIdInvalid";
}

internal sealed record ReservationRequestMappingResult(
    ProcessReservationRequest? Request,
    string? ErrorCode,
    string? CorrelationId = null)
{
    public bool IsValid => Request is not null && ErrorCode is null;

    public static ReservationRequestMappingResult Success(
        ProcessReservationRequest request,
        string correlationId) =>
        new(request, null, correlationId);

    public static ReservationRequestMappingResult Invalid(string errorCode) =>
        new(null, errorCode);
}
