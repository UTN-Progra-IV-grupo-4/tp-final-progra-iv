namespace Booking.Api.Messaging;

internal interface IReservationRequestMessageHandler
{
    Task<ReservationRequestHandlingResult> HandleAsync(
        ReservationRequestDelivery delivery,
        CancellationToken cancellationToken);
}
