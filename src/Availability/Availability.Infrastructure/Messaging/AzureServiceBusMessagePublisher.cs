using System.Text.Json;
using Availability.Application.Abstractions;
using Azure.Messaging.ServiceBus;
using Contracts;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Messaging;

public sealed class AzureServiceBusMessagePublisher : IMessagePublisher
{
    private readonly ServiceBusSender _sender;
    private readonly string _topicName;
    private readonly ILogger<AzureServiceBusMessagePublisher> _logger;

    // The sender comes from a ServiceBusClient shared for the whole process: creating a client
    // per publish opens a new AMQP connection each time, which is slow and exhausts connections.
    public AzureServiceBusMessagePublisher(
        ServiceBusClient client,
        string topicName,
        ILogger<AzureServiceBusMessagePublisher> logger)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new ArgumentException("The topic name is required.", nameof(topicName));
        }

        _topicName = topicName;
        _sender = client.CreateSender(topicName);
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromBytes(payload))
        {
            Subject = typeof(T).Name,
            ContentType = "application/json"
        };

        if (message is ReservationRequestMessage reservationRequest)
        {
            serviceBusMessage.MessageId = reservationRequest.MessageId.ToString();
            serviceBusMessage.CorrelationId = string.IsNullOrWhiteSpace(reservationRequest.CorrelationId)
                ? reservationRequest.MessageId.ToString()
                : reservationRequest.CorrelationId;
        }
        else
        {
            serviceBusMessage.MessageId = Guid.NewGuid().ToString();
            serviceBusMessage.CorrelationId = Guid.NewGuid().ToString();
        }

        await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);

        _logger.LogInformation(
            "Azure Service Bus message published to topic {TopicName}. MessageId: {MessageId}.",
            _topicName,
            serviceBusMessage.MessageId);
    }
}
