using System.Text.Json;
using Availability.Infrastructure.Messaging;
using Azure.Messaging.ServiceBus;
using Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Availability.Tests;

public class AzureServiceBusMessagePublisherTests
{
    private sealed class CapturingSender : ServiceBusSender
    {
        public List<ServiceBusMessage> Sent { get; } = [];

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingClient(CapturingSender sender) : ServiceBusClient
    {
        public string? RequestedTopic { get; private set; }

        public override ServiceBusSender CreateSender(string queueOrTopicName)
        {
            RequestedTopic = queueOrTopicName;
            return sender;
        }
    }

    private static ReservationRequestMessage CreateMessage(string correlationId) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = correlationId,
        EventId = Guid.NewGuid(),
        SeatId = Guid.NewGuid(),
        UserId = "user-1",
        RequestedAt = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task PublishAsync_SendsTheContractJsonWithBrokerIdentifiers()
    {
        var sender = new CapturingSender();
        var client = new CapturingClient(sender);
        var publisher = new AzureServiceBusMessagePublisher(
            client, "reservation-requests", NullLogger<AzureServiceBusMessagePublisher>.Instance);
        var message = CreateMessage("correlation-1");

        await publisher.PublishAsync(message);

        Assert.Equal("reservation-requests", client.RequestedTopic);
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(message.MessageId.ToString(), sent.MessageId);
        Assert.Equal("correlation-1", sent.CorrelationId);
        Assert.Equal("application/json", sent.ContentType);

        // Booking reads it with the same contract type.
        var received = sent.Body.ToObjectFromJson<ReservationRequestMessage>();
        Assert.Equal(message.MessageId, received!.MessageId);
        Assert.Equal(message.SeatId, received.SeatId);
        Assert.Equal(message.UserId, received.UserId);
        using var document = JsonDocument.Parse(sent.Body.ToMemory());
        Assert.True(document.RootElement.TryGetProperty("messageId", out _));
    }

    [Fact]
    public async Task PublishAsync_WithoutCorrelationId_UsesTheMessageId()
    {
        var sender = new CapturingSender();
        var publisher = new AzureServiceBusMessagePublisher(
            new CapturingClient(sender), "reservation-requests", NullLogger<AzureServiceBusMessagePublisher>.Instance);
        var message = CreateMessage(" ");

        await publisher.PublishAsync(message);

        Assert.Equal(message.MessageId.ToString(), Assert.Single(sender.Sent).CorrelationId);
    }
}
