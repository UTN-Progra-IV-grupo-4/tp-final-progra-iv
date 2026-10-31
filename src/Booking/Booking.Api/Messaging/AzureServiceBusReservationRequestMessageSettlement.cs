using Azure.Messaging.ServiceBus;

namespace Booking.Api.Messaging;

internal sealed class AzureServiceBusReservationRequestMessageSettlement(
    ProcessMessageEventArgs args) : IReservationRequestMessageSettlement
{
    public Task CompleteAsync(CancellationToken cancellationToken) =>
        args.CompleteMessageAsync(args.Message, cancellationToken);

    public Task DeadLetterAsync(
        string reason,
        string description,
        CancellationToken cancellationToken) =>
        args.DeadLetterMessageAsync(
            args.Message,
            reason,
            description,
            cancellationToken);
}
