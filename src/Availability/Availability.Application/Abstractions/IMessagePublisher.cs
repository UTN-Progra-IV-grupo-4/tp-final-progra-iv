namespace Availability.Application.Abstractions;

// Puerto de salida hacia el broker. Se define en Application y se implementa en Infrastructure
// (Azure Service Bus, registro en logs): es inversión de dependencias, así los casos de uso no
// conocen el broker concreto y se pueden probar con un publicador falso.
public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default);
}
