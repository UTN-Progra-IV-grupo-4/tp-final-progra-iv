using System.Text.Json;
using Availability.Application.Logging;
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

    [Fact]
    public async Task PublishAsync_LogsThePublishedEventWithTheCorrelationIdAndWithoutTheBody()
    {
        var requestId = Guid.Parse("c67cf9a8-cc6f-48c6-9f6c-a5b58b096d32");
        var logger = new RecordingLogger<AzureServiceBusMessagePublisher>();
        var publisher = new AzureServiceBusMessagePublisher(
            new CapturingClient(new CapturingSender()), "reservation-requests", logger);
        var message = new ReservationRequestMessage
        {
            MessageId = requestId,
            CorrelationId = requestId.ToString(),
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            UserId = "user-42"
        };

        await publisher.PublishAsync(message);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(AvailabilityAuditEvents.ReservationRequestPublished, entry.EventId);
        Assert.Contains("Service: Availability", entry.Message);
        Assert.Contains($"MessageId: {requestId}", entry.Message);
        Assert.Contains($"CorrelationId: {requestId}", entry.Message);
        Assert.DoesNotContain("user-42", entry.Message);
        Assert.DoesNotContain(message.SeatId.ToString(), entry.Message);
    }

    [Fact]
    public async Task PublishAsync_TagsTheMessageWithThePublisherHostOutsideTheBody()
    {
        var sender = new CapturingSender();
        var logger = new RecordingLogger<AzureServiceBusMessagePublisher>();
        var publisher = new AzureServiceBusMessagePublisher(
            new CapturingClient(sender), "reservation-requests", logger);

        await publisher.PublishAsync(CreateMessage("correlation-1"));

        var sent = Assert.Single(sender.Sent);
        Assert.Equal(Environment.MachineName, sent.ApplicationProperties[BrokerMessageProperties.PublisherHost]);
        Assert.Contains($"PublisherHost: {Environment.MachineName}", Assert.Single(logger.Entries).Message);
        // The host is metadata: the JSON body stays exactly the contract.
        using var document = JsonDocument.Parse(sent.Body.ToMemory());
        Assert.False(document.RootElement.TryGetProperty("publisherHost", out _));
    }
}
