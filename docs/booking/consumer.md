# Booking reservation consumer

Each message from an Azure Service Bus topic subscription represents one seat reservation request. Booking creates a `Retained` reservation when the seat is free and persists `Rejected/SeatTaken` when it is occupied. PostgreSQL's partial unique index decides concurrent claims across Booking replicas. The request ID is stored as `Reservation.Id`, so redelivery of the same request is idempotent.

## Configuration

The consumer is disabled by default. Set `ConnectionStrings:Booking` for PostgreSQL and these settings before enabling it in an environment with an assigned subscription:

| Setting | Purpose |
| --- | --- |
| `Broker:Provider` | `AzureServiceBus` |
| `Broker:AzureServiceBus:Enabled` | Set to `true` to start the consumer |
| `Broker:AzureServiceBus:ConnectionString` | Service Bus credential; store in User Secrets or an environment variable |
| `Broker:AzureServiceBus:TopicName` | Assigned topic |
| `Broker:AzureServiceBus:SubscriptionName` | Assigned subscription |
| `ReservationProcessing:RetentionDuration` | `00:10:00`, the agreed retention period |

For local development, use `dotnet user-secrets set <key> <value> --project src/Booking/Booking.Api` for sensitive values. Never commit credentials. Coordinate access before enabling a consumer on a shared subscription.

## Message handling

The temporary message model in `Booking.Api` expects a camelCase JSON body containing `messageId`, `correlationId`, `eventId`, `seatId`, `userId`, and `requestedAt`. The broker `MessageId` must equal the body's `messageId`. Booking maps that ID to `RequestId`; `UserId` and `RequestedAt` are currently deserialized but do not affect reservation processing.

The consumer uses manual settlement (`AutoCompleteMessages = false`):

| Result | Action |
| --- | --- |
| `Retained`, `Duplicate`, `SeatTaken` | Complete |
| Invalid message, `RequestConflict`, `InvalidRequest` | Dead-letter with a safe reason code |
| Processing or settlement exception | Propagate the exception so the broker can retry |

Logs include message, request, and correlation identifiers, delivery count, outcome, and settlement action without logging the body or credentials.

## Verification and integration status

Run `dotnet build TpFinal.sln` and `dotnet test tests/Booking/Booking.Tests/Booking.Tests.csproj` with Docker available for the PostgreSQL integration tests. Controlled Service Bus smoke tests were performed with a separate test sender; they did not exercise Availability's real publisher or two live Booking replicas.

The shared contract from #15 is still pending integration. Before replacing the private model, confirm the publisher's JSON casing, broker `MessageId`, correlation metadata, and actual Azure Service Bus publication, then run an end-to-end test from Availability to Booking.
