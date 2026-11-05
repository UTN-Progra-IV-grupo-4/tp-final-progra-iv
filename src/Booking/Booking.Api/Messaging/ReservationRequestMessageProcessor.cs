using Booking.Api.Logging;
using Booking.Business.Payments;
using Booking.Business.Reservations;
using Booking.Data.Models;

namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMessageProcessor(
    IReservationRequestMessageHandler handler,
    ILogger<ReservationRequestMessageProcessor> logger)
{
    private const string ServiceName = BookingAuditEvents.ServiceName;
    private const string PaymentOutcomeUnknownErrorCode = "PaymentOutcomeUnknown";

    public async Task ProcessAsync(
        ReservationRequestDelivery delivery,
        IReservationRequestMessageSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(settlement);

        var safeDeliveryCorrelationId = ReservationRequestMapper.IsSafeCorrelationId(delivery.CorrelationId)
            ? delivery.CorrelationId
            : null;

        logger.LogInformation(
            BookingAuditEvents.ReservationMessageReceived,
            "Reservation message received. Service: {Service}; MessageId: {MessageId}; CorrelationId: {CorrelationId}; DeliveryCount: {DeliveryCount}.",
            ServiceName,
            delivery.MessageId,
            safeDeliveryCorrelationId,
            delivery.DeliveryCount);

        ReservationRequestHandlingResult handlingResult;
        try
        {
            handlingResult = await handler.HandleAsync(delivery, cancellationToken);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                BookingAuditEvents.ReservationMessageFailed,
                "Reservation message processing was cancelled and will be retried. MessageId: {MessageId}; CorrelationId: {CorrelationId}; ExceptionType: {ExceptionType}.",
                delivery.MessageId,
                safeDeliveryCorrelationId,
                exception.GetType().FullName);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                BookingAuditEvents.ReservationMessageFailed,
                "Reservation message processing failed and will be retried. MessageId: {MessageId}; CorrelationId: {CorrelationId}; DeliveryCount: {DeliveryCount}; ExceptionType: {ExceptionType}.",
                delivery.MessageId,
                safeDeliveryCorrelationId,
                delivery.DeliveryCount,
                exception.GetType().FullName);
            throw;
        }

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);
        var correlationId = handlingResult.CorrelationId ?? safeDeliveryCorrelationId;

        try
        {
            if (decision.Action == ReservationRequestSettlementAction.Complete)
            {
                await settlement.CompleteAsync(cancellationToken);
            }
            else
            {
                await settlement.DeadLetterAsync(
                    decision.Reason!,
                    decision.Description!,
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                BookingAuditEvents.ReservationMessageFailed,
                "Reservation message settlement failed and will be retried. MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; Action: {SettlementAction}; ExceptionType: {ExceptionType}.",
                delivery.MessageId,
                handlingResult.ProcessingResult?.RequestId,
                correlationId,
                decision.Action,
                exception.GetType().FullName);
            throw;
        }

        if (decision.Action == ReservationRequestSettlementAction.Complete)
        {
            if (handlingResult.PaymentResult?.PaymentOutcomeUnknown == true)
            {
                logger.LogWarning(
                    BookingAuditEvents.PossibleLatePayment,
                    "Possible late payment; payment result unknown after bounded attempts. Service: {Service}; ErrorCode: {ErrorCode}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ChargeId: {ChargeId}; ReservationStatus: {ReservationStatus}; SettlementAction: {SettlementAction}; OccurredAtUtc: {OccurredAtUtc}; DeliveryCount: {DeliveryCount}.",
                    ServiceName,
                    PaymentOutcomeUnknownErrorCode,
                    delivery.MessageId,
                    handlingResult.ProcessingResult!.RequestId,
                    correlationId,
                    handlingResult.PaymentResult.ChargeId,
                    handlingResult.PaymentResult.CurrentStatus,
                    decision.Action,
                    DateTimeOffset.UtcNow,
                    delivery.DeliveryCount);
            }

            // ReservationStatus is the state the reservation ends in; Result is what decided it (the payment
            // outcome when there was a charge, otherwise the reservation processing outcome). Logging only the
            // processing outcome made a rejected payment read as "Retained".
            logger.LogInformation(
                BookingAuditEvents.ReservationMessageCompleted,
                "Reservation message completed. Service: {Service}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ReservationStatus: {ReservationStatus}; Result: {Result}; ChargeId: {ChargeId}; SettlementAction: {SettlementAction}.",
                ServiceName,
                delivery.MessageId,
                handlingResult.ProcessingResult!.RequestId,
                correlationId,
                ResolveFinalStatus(handlingResult),
                (object?)handlingResult.PaymentResult?.Outcome ?? handlingResult.ProcessingResult.Outcome,
                handlingResult.PaymentResult?.ChargeId,
                decision.Action);
        }
        else
        {
            if (handlingResult.PaymentFailureOutcomeUnknown)
            {
                logger.LogWarning(
                    BookingAuditEvents.PossibleLatePayment,
                    "Possible late payment; payment result unknown after bounded attempts. Service: {Service}; ErrorCode: {ErrorCode}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; PaymentFailure: {PaymentFailure}; SettlementAction: {SettlementAction}; OccurredAtUtc: {OccurredAtUtc}; DeliveryCount: {DeliveryCount}.",
                    ServiceName,
                    PaymentOutcomeUnknownErrorCode,
                    delivery.MessageId,
                    handlingResult.ProcessingResult!.RequestId,
                    correlationId,
                    handlingResult.PaymentFailure,
                    decision.Action,
                    DateTimeOffset.UtcNow,
                    delivery.DeliveryCount);
            }

            if (handlingResult.PaymentResult?.Outcome == ReservationPaymentOutcome.LateApproved)
            {
                logger.LogWarning(
                    BookingAuditEvents.LateApprovedPayment,
                    "Late approved payment requires review. Service: {Service}; ErrorCode: {ErrorCode}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ChargeId: {ChargeId}; ReservationStatus: {ReservationStatus}; SettlementAction: {SettlementAction}; OccurredAtUtc: {OccurredAtUtc}; DeliveryCount: {DeliveryCount}.",
                    ServiceName,
                    ReservationRequestSettlementErrorCodes.PaymentApprovedAfterExpiry,
                    delivery.MessageId,
                    handlingResult.ProcessingResult!.RequestId,
                    correlationId,
                    handlingResult.PaymentResult.ChargeId,
                    handlingResult.PaymentResult.CurrentStatus,
                    decision.Action,
                    DateTimeOffset.UtcNow,
                    delivery.DeliveryCount);
            }

            logger.LogWarning(
                BookingAuditEvents.ReservationMessageDeadLettered,
                "Reservation message moved to dead-letter. Service: {Service}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ReservationStatus: {ReservationStatus}; ChargeId: {ChargeId}; SettlementAction: {SettlementAction}; ErrorCode: {ErrorCode}.",
                ServiceName,
                delivery.MessageId,
                handlingResult.ProcessingResult?.RequestId,
                correlationId,
                ResolveFinalStatus(handlingResult),
                handlingResult.PaymentResult?.ChargeId,
                decision.Action,
                decision.Reason);
        }
    }

    // The state the reservation is in once the message is settled; null when it cannot be known without
    // reading the database (for example a duplicate delivery with payment disabled).
    private static ReservationStatus? ResolveFinalStatus(ReservationRequestHandlingResult handlingResult) =>
        handlingResult.PaymentResult?.CurrentStatus ?? handlingResult.ProcessingResult?.Outcome switch
        {
            ReservationProcessingOutcome.Retained => ReservationStatus.Retained,
            ReservationProcessingOutcome.SeatTaken => ReservationStatus.Rejected,
            _ => null
        };
}
