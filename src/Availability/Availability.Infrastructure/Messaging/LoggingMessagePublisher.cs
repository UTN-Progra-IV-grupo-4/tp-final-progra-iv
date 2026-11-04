using System.Text.Json;
using Availability.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Messaging;

public class LoggingMessagePublisher : IMessagePublisher
{
    private readonly ILogger<LoggingMessagePublisher> _logger;

    public LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(message);
        _logger.LogInformation("Reservation event published: {Payload}", payload);
        return Task.CompletedTask;
    }
}
