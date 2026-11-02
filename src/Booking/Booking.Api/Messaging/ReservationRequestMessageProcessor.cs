namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMessageProcessor(
    IReservationRequestMessageHandler handler,
    ILogger<ReservationRequestMessageProcessor> logger)
{
    private const string ServiceName = "Booking";
    private const string PaymentOutcomeUnknownErrorCode = "PaymentOutcomeUnknown";

    public async Task ProcessAsync(
        ReservationRequestDelivery delivery,
        IReservationRequestMessageSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(settlement);

        logger.LogInformation(
            "Reservation message received. MessageId: {MessageId}; CorrelationId: {CorrelationId}; DeliveryCount: {DeliveryCount}.",
            delivery.MessageId,
            delivery.CorrelationId,
            delivery.DeliveryCount);

        ReservationRequestHandlingResult handlingResult;
        try
        {
            handlingResult = await handler.HandleAsync(delivery, cancellationToken);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Reservation message processing was cancelled and will be retried. MessageId: {MessageId}; CorrelationId: {CorrelationId}; ExceptionType: {ExceptionType}.",
                delivery.MessageId,
                delivery.CorrelationId,
                exception.GetType().FullName);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Reservation message processing failed and will be retried. MessageId: {MessageId}; CorrelationId: {CorrelationId}; DeliveryCount: {DeliveryCount}; ExceptionType: {ExceptionType}.",
                delivery.MessageId,
                delivery.CorrelationId,
                delivery.DeliveryCount,
                exception.GetType().FullName);
            throw;
        }

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);
        var correlationId = handlingResult.CorrelationId ?? delivery.CorrelationId;

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
            if (handlingResult.PaymentResult?.Outcome ==
                Booking.Business.Payments.ReservationPaymentOutcome.PaymentUnavailable)
            {
                logger.LogWarning(
                    "Possible late payment; payment result unknown after bounded attempts. Service: {Service}; ErrorCode: {ErrorCode}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; DeliveryCount: {DeliveryCount}.",
                    ServiceName,
                    PaymentOutcomeUnknownErrorCode,
                    delivery.MessageId,
                    handlingResult.ProcessingResult!.RequestId,
                    correlationId,
                    delivery.DeliveryCount);
            }

            logger.LogInformation(
                "Reservation message completed. MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; Outcome: {Outcome}; PaymentOutcome: {PaymentOutcome}.",
                delivery.MessageId,
                handlingResult.ProcessingResult!.RequestId,
                correlationId,
                handlingResult.ProcessingResult.Outcome,
                handlingResult.PaymentResult?.Outcome);
        }
        else
        {
            if (handlingResult.PaymentResult?.Outcome ==
                Booking.Business.Payments.ReservationPaymentOutcome.LateApproved)
            {
                logger.LogWarning(
                    "Late approved payment requires review. Service: {Service}; ErrorCode: {ErrorCode}; MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ChargeId: {ChargeId}; ReservationStatus: {ReservationStatus}; DeliveryCount: {DeliveryCount}.",
                    ServiceName,
                    ReservationRequestSettlementErrorCodes.PaymentApprovedAfterExpiry,
                    delivery.MessageId,
                    handlingResult.ProcessingResult!.RequestId,
                    correlationId,
                    handlingResult.PaymentResult.ChargeId,
                    handlingResult.PaymentResult.CurrentStatus,
                    delivery.DeliveryCount);
            }

            logger.LogWarning(
                "Reservation message moved to dead-letter. MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ErrorCode: {ErrorCode}.",
                delivery.MessageId,
                handlingResult.ProcessingResult?.RequestId,
                correlationId,
                decision.Reason);
        }
    }
}
