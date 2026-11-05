# Availability: Azure Service Bus publisher (#15)

Availability publishes each reservation request to an Azure Service Bus topic. Booking consumes it
(see `docs/booking/consumer.md`); both sides share the `Contracts.ReservationRequestMessage` JSON.

## Configuration

| Key | Meaning |
|---|---|
| `Broker:Provider` | `AzureServiceBus` selects this publisher |
| `Broker:AzureServiceBus:Enabled` | `true` to publish for real. `false` (default) only logs the message, as before |
| `Broker:AzureServiceBus:ConnectionString` | Required when enabled. Use user-secrets or an environment variable, never a file in the repo |
| `Broker:AzureServiceBus:TopicName` | Topic to publish to (default `reservation-requests`); it must match Booking's `TopicName` |

```powershell
$env:Broker__AzureServiceBus__Enabled = 'true'
$env:Broker__AzureServiceBus__ConnectionString = Read-Host -MaskInput 'Service Bus connection string'
$env:Broker__AzureServiceBus__TopicName = '<topic>'
```

## Behavior

- `Enabled=true` without a connection string or topic name fails at startup, not on the first request.
- A single `ServiceBusClient` (one AMQP connection) is shared by the whole process.
- `MessageId` is the request id and `CorrelationId` is the message `correlationId`, so Booking can
  detect duplicates and trace the request.
- If the send fails, the handler removes the stored request and reports the failure
  (`ReservationRequestPublishException`).
