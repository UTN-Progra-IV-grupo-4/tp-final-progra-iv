namespace Booking.Api.Messaging;

internal interface IReservationRequestConsumer
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
