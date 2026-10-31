using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

internal static class ReservationRequestSettlementDecider
{
    private const string InvalidMessageDescription =
        "The reservation request message is invalid.";
    private const string RequestConflictDescription =
        "The request identifier is already associated with another reservation payload.";
    private const string InvalidRequestDescription =
        "The reservation request failed business validation.";

    public static ReservationRequestSettlementDecision Decide(
        ReservationRequestHandlingResult handlingResult)
    {
        ArgumentNullException.ThrowIfNull(handlingResult);

        if (!handlingResult.WasMapped)
        {
            return new ReservationRequestSettlementDecision(
                ReservationRequestSettlementAction.DeadLetter,
                handlingResult.MappingErrorCode ?? ReservationRequestMappingErrorCodes.InvalidJson,
                InvalidMessageDescription);
        }

        var processingResult = handlingResult.ProcessingResult ??
            throw new InvalidOperationException(
                "A mapped reservation request requires a processing result.");

        return processingResult.Outcome switch
        {
            ReservationProcessingOutcome.Retained or
            ReservationProcessingOutcome.Duplicate or
            ReservationProcessingOutcome.SeatTaken =>
                new ReservationRequestSettlementDecision(
                    ReservationRequestSettlementAction.Complete),
            ReservationProcessingOutcome.RequestConflict =>
                new ReservationRequestSettlementDecision(
                    ReservationRequestSettlementAction.DeadLetter,
                    processingResult.ErrorCode ??
                        ReservationProcessingErrorCodes.RequestPayloadConflict,
                    RequestConflictDescription),
            ReservationProcessingOutcome.InvalidRequest =>
                new ReservationRequestSettlementDecision(
                    ReservationRequestSettlementAction.DeadLetter,
                    processingResult.ErrorCode ??
                        ReservationRequestSettlementErrorCodes.InvalidRequest,
                    InvalidRequestDescription),
            _ => throw new InvalidOperationException(
                $"Unknown reservation processing outcome: {processingResult.Outcome}.")
        };
    }
}
