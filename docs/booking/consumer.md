# Booking reservation consumer

Each message from an Azure Service Bus topic subscription represents one seat reservation request. Booking creates a `Retained` reservation when the seat is free and persists `Rejected/SeatTaken` when it is occupied by a confirmed reservation or an unexpired retention. A `Retained` reservation whose `ExpiresAt` has passed is no longer considered an active claim: when it blocks a new insert, Booking conditionally marks it `Expired` and retries the insert once. PostgreSQL's partial unique index decides concurrent claims across Booking replicas and excludes `Expired` reservations. The request ID is stored as `Reservation.Id`, so redelivery of the same request is idempotent.

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
| `ReservationProcessing:ExpirationCheckInterval` | `00:00:30`, how often the expiration job runs |
| `ReservationProcessing:ExpirationBatchSize` | `100`, maximum expired reservations loaded per query batch |

For local development, use `dotnet user-secrets set <key> <value> --project src/Booking/Booking.Api` for sensitive values. Never commit credentials. Coordinate access before enabling a consumer on a shared subscription.

### Configuration boundaries

| Behavior | Where it is controlled | Current value or rule |
| --- | --- | --- |
| PostgreSQL connection, broker enablement, provider, credential, topic and subscription | Booking configuration (User Secrets or environment variables for sensitive values) | Broker disabled by default; provider `AzureServiceBus` |
| Reservation retention | `ReservationProcessing:RetentionDuration` | `00:10:00`, agreed with the team; a positive value is required at startup |
| Expiration job interval and batch size | `ReservationProcessing:ExpirationCheckInterval` and `ExpirationBatchSize` | `00:00:30` and `100`; positive values are required at startup |
| Message settlement | `AzureServiceBusReservationRequestConsumer` code | `AutoCompleteMessages = false`; completion or dead-letter follows the processing result, so this is not an application setting |
| Concurrency, prefetch and lock renewal | Azure Service Bus SDK processor defaults | Booking does not set `MaxConcurrentCalls`, `PrefetchCount` or `MaxAutoLockRenewalDuration`; changing them requires a code change and verification |
| Delivery limit and subscription properties | Azure Service Bus subscription | `MaxDeliveryCount` is not configured by Booking; the consumer does not assume a particular value |

The number of Booking processes is chosen when running or deploying the API. For the two-process demonstration, each process uses the same subscription and database but a different HTTP port; see [two-replica-validation.md](two-replica-validation.md).

## Retained reservation expiration

`ExpiredReservationsBackgroundService` runs an expiration pass immediately at startup and then at each configured interval. Each pass reads expired `Retained` reservations ordered by expiration and ID, in batches of `ExpirationBatchSize`, and marks each one `Expired` with a conditional database update that rechecks its status and expiration time. A partial `(expires_at, id)` index for `Retained` reservations supports this scan. Changing the state removes a reservation from the partial unique seat index, releasing the seat. The job continues fetching batches until none remain; a failure in one pass is logged and does not stop future runs.

The consumer also handles the gap before the next job run: if an insert conflicts with an expired `Retained` reservation, it attempts the same conditional expiration for that event and seat, then retries the insert once. If the reservation is not expired, another reservation still blocks the seat, or the retry conflicts again, the request remains `Rejected/SeatTaken`.

## Message handling

Booking deserializes the shared `Contracts.ReservationRequestMessage` from #15. Its camelCase JSON body contains `messageId`, `correlationId`, `eventId`, `seatId`, `userId`, and `requestedAt`. The broker `MessageId` must equal the body's `messageId`, and the body `correlationId` must be nonempty. Booking maps `messageId` to `RequestId`; `UserId` and `RequestedAt` are deserialized but do not affect reservation processing.

The consumer uses manual settlement (`AutoCompleteMessages = false`):

| Result | Action |
| --- | --- |
| `Retained`, `Duplicate`, `SeatTaken` | Complete |
| Invalid message, `RequestConflict`, `InvalidRequest` | Dead-letter with a safe reason code |
| Processing or settlement exception | Propagate the exception so the broker can retry |

Logs include message, request, and correlation identifiers, delivery count, outcome, and settlement action without logging the body or credentials.

## Verification and integration status

Run `dotnet build TpFinal.sln` and `dotnet test tests/Booking/Booking.Tests/Booking.Tests.csproj` with Docker available for the PostgreSQL integration tests. The shared contract is covered by mapper tests. A controlled test with two live Booking processes on one Azure Service Bus subscription and one PostgreSQL database is documented in [two-replica-validation.md](two-replica-validation.md), including an opt-in sender under `tests/Booking/` and the observed results.

The real Availability publisher from #15 still uses a logging placeholder rather than sending to Azure Service Bus. The two-replica test therefore proves Booking's consumer behavior with the shared message type but does not prove end-to-end publication from Availability. Confirm the publisher's actual broker `MessageId`, `CorrelationId`, and JSON once its Azure Service Bus implementation is available. Issue #18 also lists SDK decisions as pending, without specifying final values.

The two-replica observation validates this configuration for the small coordinated test only. Issue #18 does not prescribe values for the SDK processor settings; any change to those defaults should be justified and verified against the required workload.
