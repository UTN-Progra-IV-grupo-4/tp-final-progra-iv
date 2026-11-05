using System.Text.Json;
using Availability.Application.Abstractions;
using Azure.Messaging.ServiceBus;
using Contracts;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Messaging;

public sealed class AzureServiceBusMessagePublisher : IMessagePublisher
{
    private readonly string _connectionString;
    private readonly string _topicName;
    private readonly ILogger<AzureServiceBusMessagePublisher> _logger;

    public AzureServiceBusMessagePublisher(
        string? connectionString,
        string? topicName,
        ILogger<AzureServiceBusMessagePublisher> logger)
    {
        _connectionString = connectionString ?? string.Empty;
        _topicName = string.IsNullOrWhiteSpace(topicName) ? "reservation-requests" : topicName;
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "Broker:AzureServiceBus:ConnectionString is required when Azure Service Bus is enabled.");
        }

        if (string.IsNullOrWhiteSpace(_topicName))
        {
            throw new InvalidOperationException(
                "Broker:AzureServiceBus:TopicName is required when Azure Service Bus is enabled.");
        }

        await using var client = new ServiceBusClient(_connectionString);
        await using var sender = client.CreateSender(_topicName);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromBytes(payload))
        {
            Subject = typeof(T).Name
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

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);

        _logger.LogInformation(
            "Azure Service Bus message published to topic {TopicName}. MessageId: {MessageId}.",
            _topicName,
            serviceBusMessage.MessageId);
    }
}
