using Booking.Business.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMessageHandler(
    IServiceScopeFactory scopeFactory,
    ReservationRequestMapper mapper)
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

        return ReservationRequestHandlingResult.Processed(processingResult);
    }
}
