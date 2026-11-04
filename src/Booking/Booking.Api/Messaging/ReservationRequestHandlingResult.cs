using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

internal sealed record ReservationRequestHandlingResult(
    ReservationProcessingResult? ProcessingResult,
    string? MappingErrorCode)
{
    public bool WasMapped => ProcessingResult is not null && MappingErrorCode is null;

    public static ReservationRequestHandlingResult Processed(
        ReservationProcessingResult processingResult) =>
        new(processingResult, null);

    public static ReservationRequestHandlingResult Invalid(string errorCode) =>
        new(null, errorCode);
}
