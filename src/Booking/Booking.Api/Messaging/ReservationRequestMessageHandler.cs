using Booking.Business.Payments;
using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMessageHandler(
    IServiceScopeFactory scopeFactory,
    ReservationRequestMapper mapper,
    IOptions<PaymentConsumerOptions> paymentOptions) : IReservationRequestMessageHandler
{
    public async Task<ReservationRequestHandlingResult> HandleAsync(
        ReservationRequestDelivery delivery,
        CancellationToken cancellationToken)
    {
        var mappingResult = mapper.Map(delivery);
        if (!mappingResult.IsValid)
        {
            return ReservationRequestHandlingResult.Invalid(
                mappingResult.ErrorCode ?? ReservationRequestMappingErrorCodes.InvalidJson);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IReservationRequestProcessor>();
        var processingResult = await processor.ProcessAsync(
            mappingResult.Request!,
            cancellationToken);

        if (!paymentOptions.Value.Enabled || processingResult.Outcome is not (
            ReservationProcessingOutcome.Retained or ReservationProcessingOutcome.Duplicate))
        {
            return ReservationRequestHandlingResult.Processed(
                processingResult,
                correlationId: mappingResult.CorrelationId);
        }

        var paymentProcessor = scope.ServiceProvider.GetRequiredService<ReservationPaymentProcessor>();
        try
        {
            var paymentResult = await paymentProcessor.ProcessAsync(
                processingResult.RequestId,
                mappingResult.CorrelationId,
                cancellationToken);

            if (paymentResult.Outcome == ReservationPaymentOutcome.NotFound)
            {
                throw new InvalidOperationException("A retained reservation was not found before payment.");
            }

            return ReservationRequestHandlingResult.Processed(
                processingResult,
                paymentResult,
                correlationId: mappingResult.CorrelationId);
        }
        catch (PaymentChargeException exception) when (exception.Failure is
            PaymentChargeFailure.Pending or
            PaymentChargeFailure.InvalidRequest or
            PaymentChargeFailure.UnexpectedResponse)
        {
            // These outcomes require manual review; no payment terminal state was persisted.
            return ReservationRequestHandlingResult.Processed(
                processingResult,
                paymentFailure: exception.Failure,
                paymentFailureOutcomeUnknown: exception.PaymentOutcomeUnknown,
                correlationId: mappingResult.CorrelationId);
        }
    }
}
