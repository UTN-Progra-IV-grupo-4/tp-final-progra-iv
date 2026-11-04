using Availability.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Messaging;

public class RabbitMqMessagePublisher : IMessagePublisher
{
    private readonly ILogger<RabbitMqMessagePublisher> _logger;

    public RabbitMqMessagePublisher(ILogger<RabbitMqMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RabbitMQ publish placeholder for message: {Message}", message);
        return Task.CompletedTask;
    }
}
