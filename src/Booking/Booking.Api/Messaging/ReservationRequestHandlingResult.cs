using Booking.Business.Payments;
using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

internal sealed record ReservationRequestHandlingResult(
    ReservationProcessingResult? ProcessingResult,
    string? MappingErrorCode,
    ReservationPaymentResult? PaymentResult = null,
    PaymentChargeFailure? PaymentFailure = null,
    string? CorrelationId = null,
    bool PaymentFailureOutcomeUnknown = false)
{
    public bool WasMapped => ProcessingResult is not null && MappingErrorCode is null;

    public static ReservationRequestHandlingResult Processed(
        ReservationProcessingResult processingResult,
        ReservationPaymentResult? paymentResult = null,
        PaymentChargeFailure? paymentFailure = null,
        string? correlationId = null,
        bool paymentFailureOutcomeUnknown = false) =>
        new(processingResult, null, paymentResult, paymentFailure, correlationId, paymentFailureOutcomeUnknown);

    public static ReservationRequestHandlingResult Invalid(string errorCode) =>
        new(null, errorCode);
}
