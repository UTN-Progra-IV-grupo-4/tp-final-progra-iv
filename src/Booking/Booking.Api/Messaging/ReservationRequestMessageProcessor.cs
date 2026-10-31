namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMessageProcessor(
    IReservationRequestMessageHandler handler,
    ILogger<ReservationRequestMessageProcessor> logger)
{
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
                delivery.CorrelationId,
                decision.Action,
                exception.GetType().FullName);
            throw;
        }

        if (decision.Action == ReservationRequestSettlementAction.Complete)
        {
            logger.LogInformation(
                "Reservation message completed. MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; Outcome: {Outcome}.",
                delivery.MessageId,
                handlingResult.ProcessingResult!.RequestId,
                delivery.CorrelationId,
                handlingResult.ProcessingResult.Outcome);
        }
        else
        {
            logger.LogWarning(
                "Reservation message moved to dead-letter. MessageId: {MessageId}; RequestId: {RequestId}; CorrelationId: {CorrelationId}; ErrorCode: {ErrorCode}.",
                delivery.MessageId,
                handlingResult.ProcessingResult?.RequestId,
                delivery.CorrelationId,
                decision.Reason);
        }
    }
}
