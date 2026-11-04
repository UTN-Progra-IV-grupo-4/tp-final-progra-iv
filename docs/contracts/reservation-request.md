# Reservation Request Contract

Este contrato representa la solicitud de reserva de un asiento emitida por `Availability` y consumida por `Booking` a través de mensajería asíncrona.

## Campos

- `MessageId`: GUID único de la solicitud. Debe usarse también como `ServiceBusMessage.MessageId` del broker para que Booking valide la coincidencia del cuerpo y del mensaje, y lo use como identificador idempotente de la reserva.
- `CorrelationId`: identificador para rastreo distribuido. Availability usa el mismo valor que el identificador de la solicitud (`MessageId` en texto). Decisión pendiente de confirmar con #24.
- `EventId`: identificador único del evento.
- `SeatId`: identificador único del asiento.
- `UserId`: usuario que solicita la reserva.
- `RequestedAt`: fecha y hora UTC en la que se emitió la solicitud.

## Endpoint `POST /api/reservations`

Requiere un usuario autenticado; el `UserId` del mensaje sale de su claim `sub`.

| Código | Cuándo |
| --- | --- |
| 202 | Solicitud almacenada y publicada. Devuelve `requestId` y `status` ("Pending"). |
| 400 | El asiento no existe o no pertenece al evento indicado. |
| 401 | No hay credenciales válidas o el usuario no tiene identificador. |
| 503 | No se pudo publicar en el broker. Se devuelve Problem Details sin detalles internos. |

## Fallo de publicación

Availability guarda la solicitud antes de publicar. Si la publicación falla, elimina la solicitud guardada y responde 503: Booking nunca recibió el mensaje y Availability interpreta el 404 de Booking como "en proceso", por lo que dejar la fila `Pending` haría esperar al usuario indefinidamente. El cliente puede reintentar con una nueva solicitud.

## Diagrama Mermaid

```mermaid
sequenceDiagram
    participant A as Availability
    participant B as Broker/Async Channel
    participant C as Booking

    A->>B: Publish ReservationRequestMessage
    Note over A,B: MessageId, CorrelationId, EventId, SeatId, UserId, RequestedAt
    B->>C: Deliver ReservationRequestMessage
    Note over A,C: Result is not sent over the broker; it is queried later via the reservation-status contract.
```

## Modelo C#

```csharp
public class ReservationRequestMessage
{
    public Guid MessageId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public Guid SeatId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}
```

## Ejemplo de payload

```json
{
  "messageId": "7b2f0c31-9d3a-4a4f-8d2e-0f6b11f5a245",
  "correlationId": "req-2026-10-31-001",
  "eventId": "8cc3b719-4866-4f8b-a893-6db7d0f18b55",
  "seatId": "1cf6fb92-e01e-4d7c-8935-14a42f8fd17a",
  "userId": "user-42",
  "requestedAt": "2026-10-31T12:00:00Z"
}
```
