namespace Booking.Api.Messaging;

internal interface IReservationRequestMessageSettlement
{
    Task CompleteAsync(CancellationToken cancellationToken);

    Task DeadLetterAsync(
        string reason,
        string description,
        CancellationToken cancellationToken);
}
