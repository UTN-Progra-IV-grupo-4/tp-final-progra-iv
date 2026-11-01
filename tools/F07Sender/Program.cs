using System.Text.Json;
using Azure.Messaging.ServiceBus;

var connectionString = RequireEnvironment("F07_ASB_CONNECTION_STRING");
var topicName = RequireEnvironment("F07_ASB_TOPIC");
var scenario = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "new";

if (scenario is "peek-dlq" or "peek-active")
{
    var subscriptionName = RequireEnvironment("F07_ASB_SUBSCRIPTION");
    await using var inspectionClient = new ServiceBusClient(connectionString);
    await using var receiver = inspectionClient.CreateReceiver(
        topicName,
        subscriptionName,
        new ServiceBusReceiverOptions
        {
            SubQueue = scenario == "peek-dlq" ? SubQueue.DeadLetter : SubQueue.None
        });
    var messages = await receiver.PeekMessagesAsync(20);
    Console.WriteLine($"{scenario}: mensajes visibles: {messages.Count}");
    foreach (var item in messages)
    {
        Console.WriteLine($"MessageId={item.MessageId}; SequenceNumber={item.SequenceNumber}");
    }
    return;
}

if (scenario == "cleanup-dlq")
{
    var subscriptionName = RequireEnvironment("F07_ASB_SUBSCRIPTION");
    var expectedIds = RequireEnvironment("F07_CLEANUP_MESSAGE_IDS")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    await using var cleanupClient = new ServiceBusClient(connectionString);
    await using var receiver = cleanupClient.CreateReceiver(
        topicName,
        subscriptionName,
        new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

    var messages = await receiver.ReceiveMessagesAsync(20, TimeSpan.FromSeconds(5));
    foreach (var item in messages)
    {
        if (expectedIds.Remove(item.MessageId))
        {
            await receiver.CompleteMessageAsync(item);
            Console.WriteLine($"Dead-letter de prueba eliminado: MessageId={item.MessageId}");
        }
        else
        {
            await receiver.AbandonMessageAsync(item);
        }
    }

    if (expectedIds.Count > 0)
    {
        throw new InvalidOperationException(
            $"No se encontraron todos los MessageId de prueba: {string.Join(',', expectedIds)}");
    }

    return;
}

var requestId = ParseOrCreateGuid("F07_REQUEST_ID");
var eventId = ParseOrCreateGuid("F07_EVENT_ID");
var seatId = ParseOrCreateGuid("F07_SEAT_ID");
var correlationId = ParseOrCreateGuid("F07_CORRELATION_ID");

var body = scenario switch
{
    "invalid" => "{\"messageId\":\"not-a-guid\"}",
    _ => JsonSerializer.Serialize(new
    {
        messageId = requestId,
        correlationId = correlationId.ToString(),
        eventId,
        seatId,
        userId = "f07-test-user",
        requestedAt = DateTime.UtcNow
    })
};

var message = new ServiceBusMessage(body)
{
    MessageId = requestId.ToString(),
    CorrelationId = correlationId.ToString(),
    Subject = "ReservationRequested",
    ContentType = "application/json"
};

await using var client = new ServiceBusClient(connectionString);
await using var sender = client.CreateSender(topicName);
await sender.SendMessageAsync(message);

Console.WriteLine($"Mensaje enviado. Scenario={scenario}; MessageId={message.MessageId}; RequestId={requestId}; EventId={eventId}; SeatId={seatId}; CorrelationId={correlationId}");

static string RequireEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Falta la variable de entorno {name}.");

static Guid ParseOrCreateGuid(string name) =>
    Guid.TryParse(Environment.GetEnvironmentVariable(name), out var value)
        ? value
        : Guid.NewGuid();
