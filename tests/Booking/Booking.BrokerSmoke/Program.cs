using Azure.Messaging.ServiceBus;
using Contracts;

// Opt-in smoke sender. Run only during a coordinated window on an assigned topic/subscription.
var connectionString = RequireEnvironment("BOOKING_SMOKE_ASB_CONNECTION_STRING");
var topicName = RequireEnvironment("BOOKING_SMOKE_ASB_TOPIC");
var operation = args.FirstOrDefault()?.ToLowerInvariant() ?? "send-pair";

if (operation is "peek-active" or "peek-dlq")
{
    var subscriptionName = RequireEnvironment("BOOKING_SMOKE_ASB_SUBSCRIPTION");
    await using var inspectionClient = new ServiceBusClient(connectionString);
    await using var receiver = inspectionClient.CreateReceiver(
        topicName,
        subscriptionName,
        new ServiceBusReceiverOptions
        {
            SubQueue = operation == "peek-dlq" ? SubQueue.DeadLetter : SubQueue.None
        });
    var messages = await receiver.PeekMessagesAsync(20);
    Console.WriteLine($"{operation}: {messages.Count} visible messages (up to 20).");
    foreach (var message in messages)
    {
        Console.WriteLine($"MessageId={message.MessageId}; SequenceNumber={message.SequenceNumber}");
    }

    return;
}

if (operation != "send-pair")
{
    throw new ArgumentException("Use send-pair, peek-active or peek-dlq.");
}

var eventId = Guid.NewGuid();
var seatId = Guid.NewGuid();
var firstRequestId = Guid.NewGuid();
var secondRequestId = Guid.NewGuid();

ServiceBusMessage CreateMessage(Guid requestId)
{
    var payload = new ReservationRequestMessage
    {
        MessageId = requestId,
        CorrelationId = requestId.ToString(),
        EventId = eventId,
        SeatId = seatId,
        UserId = "booking-smoke-test",
        RequestedAt = DateTime.UtcNow
    };

    return new ServiceBusMessage(BinaryData.FromObjectAsJson(payload))
    {
        MessageId = requestId.ToString(),
        CorrelationId = payload.CorrelationId,
        ContentType = "application/json"
    };
}

Console.WriteLine($"EventId={eventId}; SeatId={seatId}");
Console.WriteLine($"RequestIdA={firstRequestId}; RequestIdB={secondRequestId}");

await using var client = new ServiceBusClient(connectionString);
await using var sender = client.CreateSender(topicName);
await Task.WhenAll(
    sender.SendMessageAsync(CreateMessage(firstRequestId)),
    sender.SendMessageAsync(CreateMessage(secondRequestId)));

Console.WriteLine("Both test messages were sent. Confirm each MessageId and its result in the two Booking logs and PostgreSQL.");

static string RequireEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Set {name} for the coordinated smoke test.");
