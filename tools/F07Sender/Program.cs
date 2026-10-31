using System.Text.Json;
using Azure.Messaging.ServiceBus;

var connectionString = RequireEnvironment("F07_ASB_CONNECTION_STRING");
var topicName = RequireEnvironment("F07_ASB_TOPIC");
var scenario = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "new";

var requestId = ParseOrCreateGuid("F07_REQUEST_ID");
var eventId = ParseOrCreateGuid("F07_EVENT_ID");
var seatId = ParseOrCreateGuid("F07_SEAT_ID");
var correlationId = ParseOrCreateGuid("F07_CORRELATION_ID");

var body = scenario switch
{
    "invalid" => "{\"requestId\":\"not-a-guid\"}",
    _ => JsonSerializer.Serialize(new
    {
        requestId,
        eventId,
        seatId
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
