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

## "Solicitud recibida" screen

Flow in the browser (MVC, Spanish UI). It uses the same `ReservationRequestHandler` as
`POST /api/reservations`, which is unchanged.

| Step | Route | Behavior |
|---|---|---|
| 1 | `GET /eventos/{eventId}/asientos?selectedSeatId=...` | With one selectable seat chosen, the page shows **Reservar este asiento** (a `POST` form with anti-forgery token). Not shown without a selection or for an occupied/unconfirmed seat |
| 2 | `POST /eventos/{eventId}/reservar` (`seatId`) | Validates the token, requires a logged-in user, stores the request, publishes the message and answers `303` to the received page |
| 3 | `GET /reservas/{requestId}/recibida` | Confirms the request was received and is being processed (not confirmed), shows the request id, event, seat and a link to `/reservas/{requestId}/estado` |

- Anonymous `POST` => redirect to `/Account/Login?returnUrl=` the same seat selection (a local URL built by
  the server; the login only accepts local return URLs). Nothing is stored or published.
- The received page is owner-only: a missing, malformed or foreign request id answers an empty `404`. Both
  pages send `Cache-Control: no-store`.
- Seat that no longer exists or does not belong to the event => back to the seat map with a message; nothing is stored.
- Broker failure (`ReservationRequestPublishException`, the stored request is removed) => `503` page
  "No pudimos enviar tu solicitud, intentá de nuevo" with a link back to the seat selection, without broker details.
  The API keeps answering `503` ProblemDetails.
