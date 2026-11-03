namespace Booking.Api.Messaging;

internal sealed class ReservationRequestConsumerHostedService(
    IReservationRequestConsumer consumer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        consumer.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        consumer.StopAsync(cancellationToken);
}
