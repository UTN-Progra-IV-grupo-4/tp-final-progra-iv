# Booking: two-replica consumer validation

This is an opt-in smoke test for issue #18. Two Booking.Api processes use the same Azure Service Bus topic/subscription and the same PostgreSQL database. The test sends two different request IDs for one event and seat using the shared `Contracts.ReservationRequestMessage`. It verifies broker distribution, manual settlement, and the PostgreSQL seat constraint. It does not exercise Availability's current logging-only publisher.

## Preconditions

- Coordinate a window on an assigned subscription with no other consumers or unrelated active messages. Do not run this test automatically in CI against the shared subscription.
- Start PostgreSQL with `docker compose up -d`, configure `ConnectionStrings:Booking`, and apply the existing Booking migrations with `dotnet ef database update --project src/Booking/Booking.Data --startup-project src/Booking/Booking.Api`. See the [repository README](../../README.md) for the database setup. The connection string must point to that instance.
- Configure `ConnectionStrings:Booking`, `Broker:Provider`, and the Azure Service Bus connection string, topic, and subscription in Booking.Api User Secrets or environment variables. Do not commit credentials.
- From the repository root, run `dotnet build TpFinal.sln` and `dotnet build tests/Booking/Booking.BrokerSmoke/Booking.BrokerSmoke.csproj`.

The automated Booking tests need Docker for their PostgreSQL integration cases but do not need Azure credentials. This live smoke test additionally requires access to the assigned Service Bus subscription and a coordinated window; it is not a self-contained CI test.

## Run

In a sender terminal, load credentials supplied for the coordinated test. The value entered by `Read-Host -MaskInput` is not displayed:

```powershell
$env:BOOKING_SMOKE_ASB_CONNECTION_STRING = Read-Host 'Azure Service Bus connection string' -MaskInput
$env:BOOKING_SMOKE_ASB_TOPIC = 'grupo4-booking-topic'
$env:BOOKING_SMOKE_ASB_SUBSCRIPTION = 'booking'
dotnet run --no-build --project .\tests\Booking\Booking.BrokerSmoke\Booking.BrokerSmoke.csproj -- peek-active
```

Stop if the subscription has unrelated active messages. Keep any pre-existing dead-letter messages untouched. In two separate terminals, start Booking against the same User Secrets and database:

```powershell
# Terminal A
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Broker__AzureServiceBus__Enabled = 'true'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5088'
dotnet run --no-build --no-launch-profile --project .\src\Booking\Booking.Api\Booking.Api.csproj
```

```powershell
# Terminal B
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Broker__AzureServiceBus__Enabled = 'true'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5089'
dotnet run --no-build --no-launch-profile --project .\src\Booking\Booking.Api\Booking.Api.csproj
```

Wait until both processes log that the consumer started. Then run in the sender terminal:

```powershell
dotnet run --no-build --project .\tests\Booking\Booking.BrokerSmoke\Booking.BrokerSmoke.csproj -- send-pair
```

The sender prints the event, seat, and two request IDs before sending. It starts both sends concurrently, but Azure Service Bus does not guarantee which replica receives each message. The first valid claim should become `Retained`; the other should become `Rejected/SeatTaken`. Check both console logs for each `MessageId`, `DeliveryCount`, `Outcome`, and completion. Query only the printed request IDs in Booking's `reservations` table:

```sql
SELECT id, event_id, seat_id, status, rejection_reason, expires_at
FROM reservations
WHERE id IN ('<RequestIdA>', '<RequestIdB>')
ORDER BY id;
```

For direct proof that both replicas participated in the seat conflict, one request must appear in A's log and the other in B's log. If both go to the same process, the database result is still valid, but repeat with a new pair to obtain cross-process evidence. The existing PostgreSQL integration tests independently verify simultaneous database writes without requiring Azure credentials.

After recording the result, run `peek-active` again and optionally `peek-dlq` with the sender. Stop both Booking processes. Clean up only rows and dead-letter messages whose IDs were generated for this run; never settle unrelated messages.

## Observed execution: 2026-11-04

The test used local PostgreSQL 16, `grupo4-booking-topic/booking`, two Booking.Api processes on ports 5088 and 5089, and the sender from `tests/Booking/Booking.BrokerSmoke`. The working branch was based on `347ff96` with the shared-contract replacement still uncommitted; record the final commit SHA after review.

| Instance | MessageId / RequestId | Consumer log | PostgreSQL result |
| --- | --- | --- | --- |
| A (`5088`) | `259dc2ed-60fa-4aba-86ee-b0f8eaf5ee6f` | `DeliveryCount: 1`, `Outcome: SeatTaken`, completed | `Rejected`, `SeatTaken` |
| B (`5089`) | `32ff266a-9db8-41fb-b6cc-8402601d1bd0` | `DeliveryCount: 1`, `Outcome: Retained`, completed | `Retained`, expiry `2026-11-04 03:45:05 UTC` |

Both rows had `EventId=08bddb69-f132-412d-b5d2-2ef37e665f33` and `SeatId=554b9a94-fef4-4fa2-8e55-2e4d3037be10`. The active subscription was empty before and after this run. Two pre-existing dead-letter messages with different IDs remained untouched. Both Booking processes were stopped, and the two local reservation rows from this run were deleted after the result was recorded. No credentials, user details, or message bodies are included in this record.

This demonstrates two competing Booking consumers and one seat winner. The real Availability-to-Service-Bus publisher path remains a separate integration check when #15 publishes to Azure Service Bus.
