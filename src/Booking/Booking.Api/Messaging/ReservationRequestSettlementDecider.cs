using Booking.Business.Payments;
using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

// Traduce el resultado de negocio a una acción sobre el mensaje del broker. Esta política vive en
// Api porque es parte del adaptador de mensajería: Business no sabe qué es completar ni mandar a
// dead-letter un mensaje.
// - Complete: el mensaje ya tiene un resultado definitivo (reserva retenida, duplicada, rechazada,
//   confirmada o vencida); reprocesarlo no cambiaría nada.
// - DeadLetter: reentregarlo nunca lo resolvería y requiere revisión manual (mensaje inválido, id de
//   solicitud con otro contenido, cobro pendiente o inesperado, pago aprobado fuera de plazo).
// - Reentrega: los fallos transitorios (base caída, excepción inesperada) no llegan acá; el
//   procesador relanza la excepción, el mensaje queda sin completar y el broker lo reentrega.
internal static class ReservationRequestSettlementDecider
{
    private const string InvalidMessageDescription =
        "The reservation request message is invalid.";
    private const string RequestConflictDescription =
        "The request identifier is already associated with another reservation payload.";
    private const string InvalidRequestDescription =
        "The reservation request failed business validation.";
    private const string PaymentReviewDescription =
        "The payment outcome requires manual review.";

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

        if (handlingResult.PaymentFailure is { } paymentFailure)
        {
            var reason = paymentFailure switch
            {
                PaymentChargeFailure.Pending => ReservationRequestSettlementErrorCodes.PaymentPending,
                PaymentChargeFailure.InvalidRequest => ReservationRequestSettlementErrorCodes.PaymentInvalidRequest,
                PaymentChargeFailure.UnexpectedResponse => ReservationRequestSettlementErrorCodes.PaymentUnexpectedResponse,
                _ => throw new InvalidOperationException(
                    $"Unexpected payment failure for settlement: {paymentFailure}.")
            };

            return new ReservationRequestSettlementDecision(
                ReservationRequestSettlementAction.DeadLetter,
                reason,
                PaymentReviewDescription);
        }

        if (handlingResult.PaymentResult is { } paymentResult)
        {
            return paymentResult.Outcome switch
            {
                ReservationPaymentOutcome.Confirmed or
                ReservationPaymentOutcome.PaymentFailed or
                ReservationPaymentOutcome.PaymentUnavailable or
                ReservationPaymentOutcome.Expired or
                ReservationPaymentOutcome.AlreadyTerminal =>
                    new ReservationRequestSettlementDecision(ReservationRequestSettlementAction.Complete),
                ReservationPaymentOutcome.LateApproved =>
                    new ReservationRequestSettlementDecision(
                        ReservationRequestSettlementAction.DeadLetter,
                        ReservationRequestSettlementErrorCodes.PaymentApprovedAfterExpiry,
                        PaymentReviewDescription),
                _ => throw new InvalidOperationException(
                    $"Unexpected payment outcome for settlement: {paymentResult.Outcome}.")
            };
        }

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
