# Flujo feliz y caminos de falla (demo)

Dos diagramas de secuencia para la demo: el recorrido completo de una reserva exitosa y los cuatro caminos de falla que el sistema distingue. Los valores por defecto que aparecen (retención de 10 minutos, 3 intentos de pago, deadline de 5 segundos, polling cada 3 segundos) salen de la configuración del código y se pueden cambiar.

## Cómo viaja el correlation id

Availability genera el id de la solicitud y lo usa como `MessageId`, como `correlationId` del mensaje y como id de la reserva en Booking. Ese mismo valor es el que se ve en los tres servicios.

| Tramo | Medio | Nombre | Estado en `develop` |
| --- | --- | --- | --- |
| Usuario a Availability | HTTP | no lo manda: Availability lo genera | Implementado. |
| Availability a Booking (mensaje) | Broker | `correlationId` del cuerpo y `CorrelationId` del mensaje de Service Bus | Implementado. Booking lo valida (hasta 64 caracteres ASCII: letras, dígitos, `-`, `_`, `.`) y rechaza a dead-letter el inválido. |
| Booking a Payment | gRPC | metadata `x-correlation-id` | Implementado. Payment lo agrega a sus logs, o lo registra como `invalido`. |
| Availability a Booking (consulta de estado) | HTTP | cabecera `x-correlation-id` con el id de la solicitud | **Pendiente**: la agrega el ticket #24, con el PR abierto y todavía sin integrar en `develop`. |

Los eventos de auditoría con id numérico fijo (1001 a 1003 en Availability, 2001 a 2012 en Booking, 3001 a 3004 en Payment) también pertenecen al ticket #24 y están descritos en `docs/24-logs-de-auditoria.md` de esa rama. En `develop` ya existen logs estructurados con `MessageId`, `RequestId`, `CorrelationId` y `ErrorCode` en Booking, pero sin esos ids. Los diagramas los nombran entre paréntesis, marcados como "ticket 24".

## (a) Camino feliz

Login OIDC, mapa de asientos, reserva, "solicitud recibida", retención, cobro por gRPC, confirmación y página de estado.

```mermaid
sequenceDiagram
    autonumber
    actor user as Usuario (navegador)
    participant avail as Availability
    participant kc as Keycloak
    participant bus as Azure Service Bus
    participant book as Booking
    participant bookdb as PostgreSQL de Booking
    participant pay as Payment

    Note over user,kc: Login OIDC con el client público availability-web
    user->>avail: GET /Account/Login
    avail-->>user: redirige a Keycloak
    user->>kc: login (authorization code + PKCE)
    kc-->>user: redirige a /signin-oidc con el code
    user->>avail: /signin-oidc
    avail->>kc: canjea el code por los tokens
    avail-->>user: cookie de sesión

    Note over user,book: Mapa de asientos: público, no exige login
    user->>avail: GET /eventos/{eventId}/asientos
    avail->>book: GET /api/events/{eventId}/occupied-seats con token availability-service
    book->>bookdb: asientos ocupados del evento
    book-->>avail: 200 con los seatIds ocupados
    avail-->>user: mapa con el asiento libre

    Note over user,bus: Solicitud de reserva
    user->>avail: POST /eventos/{eventId}/reservar (cookie y anti-forgery)
    avail->>avail: guarda reservation_requests, el id es el requestId y el correlationId
    avail->>bus: publica ReservationRequestMessage (MessageId y correlationId = requestId)
    avail-->>user: 303 a /reservas/{requestId}/recibida (la API responde 202)
    Note over avail,book: Consistencia eventual: el asiento sigue figurando libre hasta que Booking procese el mensaje (ticket 24: eventos 1001 y 1002)

    Note over bus,pay: Procesamiento asíncrono en Booking
    bus->>book: entrega el mensaje (AMQP, una sola réplica)
    book->>bookdb: INSERT Retained con ExpiresAt a 10 minutos
    Note over book,bookdb: el índice único parcial por evento y asiento da un solo ganador (ticket 24: evento 2001)
    book->>pay: gRPC Charge(reservation_id, amount_cents, currency)
    Note over book,pay: token booking-service en Authorization y correlationId en la metadata x-correlation-id
    pay->>pay: guarda el cobro Pending, la pasarela simulada aprueba y lo marca Approved
    pay-->>book: OK con APPROVED y charge_id
    book->>bookdb: UPDATE a Confirmed solo si sigue Retained y no venció
    book->>bus: complete del mensaje
    Note over book,pay: ticket 24: eventos 2010, 2011 y 2002 en Booking, 3001 y 3002 en Payment

    Note over user,book: Página de estado con polling cada 3 s, hasta 2 min
    loop mientras figure Procesando o Retenida
        user->>avail: GET /reservas/{requestId}/estado
        avail->>book: GET /api/reservations/{requestId}/status con token availability-service
        alt Booking todavía no registró la solicitud
            book-->>avail: 404
            avail-->>user: Procesando
        else Retained
            book-->>avail: 200 Retained
            avail-->>user: Retenida
        end
    end
    user->>avail: GET /reservas/{requestId}/estado
    avail->>book: GET /api/reservations/{requestId}/status
    book-->>avail: 200 Confirmed
    avail-->>user: Confirmada
```

Notas:

- Availability verifica que el usuario sea el dueño de la solicitud antes de preguntarle a Booking; Booking no recibe el `userId` ni controla la propiedad.
- Si el broker rechaza el mensaje, Availability elimina la solicitud guardada y responde 503, para que la solicitud no quede "procesando" para siempre.
- Booking completa el mensaje solo después de persistir el resultado.

## (b) Caminos de falla

Cuatro casos que la demo puede forzar. Todos arrancan cuando el mensaje ya llegó a Booking. En cada uno se indica qué ve el usuario en la página de estado.

```mermaid
sequenceDiagram
    autonumber
    participant avail as Availability
    participant bus as Azure Service Bus
    participant book as Booking
    participant bookdb as PostgreSQL de Booking
    participant pay as Payment

    Note over avail,pay: El correlationId (igual al requestId) acompaña al mensaje, a los logs de Booking y a la metadata x-correlation-id hacia Payment

    rect rgb(255, 244, 230)
        Note over bus,pay: 1. Asiento ocupado: otro usuario ya lo retuvo
        bus->>book: mensaje de la solicitud B para el mismo asiento
        book->>bookdb: INSERT Retained
        bookdb-->>book: viola el índice único parcial (evento y asiento)
        book->>bookdb: intenta vencer una retención ya vencida de ese asiento
        bookdb-->>book: no hay ninguna vencida
        book->>bookdb: INSERT Rejected con motivo SeatTaken
        book->>bus: complete del mensaje
        Note over book,pay: no se llama a Payment, no hay cobro
        Note over avail: el usuario ve Rechazada, el asiento ya estaba ocupado
    end

    rect rgb(255, 244, 230)
        Note over bus,pay: 2. Pago rechazado
        bus->>book: mensaje de la solicitud
        book->>bookdb: INSERT Retained
        book->>pay: gRPC Charge
        pay-->>book: OK con DECLINED (fondos insuficientes)
        book->>bookdb: UPDATE a Rejected con motivo PaymentFailed
        book->>bus: complete del mensaje
        Note over book,bookdb: el asiento queda liberado porque Rejected sale del índice único
        Note over avail: el usuario ve Rechazada, el pago fue rechazado
    end

    rect rgb(255, 244, 230)
        Note over bus,pay: 3. Payment no disponible (3 intentos, la misma clave reservation_id)
        bus->>book: mensaje de la solicitud
        book->>bookdb: INSERT Retained
        loop hasta 3 intentos, deadline de 5 s y 1 s entre intentos
            book->>pay: gRPC Charge
            pay-->>book: UNAVAILABLE o DEADLINE_EXCEEDED
        end
        book->>bookdb: UPDATE a Rejected con motivo PaymentUnavailable si sigue elegible
        book->>bus: complete del mensaje
        Note over book,pay: log de aviso PaymentOutcomeUnknown, posible cobro tardío que se coteja por reservation_id
        Note over avail: el usuario ve Rechazada, el servicio de pagos no estaba disponible
    end

    rect rgb(255, 244, 230)
        Note over bus,pay: 4. La retención vence y llega un cobro aprobado tarde
        bus->>book: mensaje de la solicitud
        book->>bookdb: INSERT Retained con ExpiresAt a 10 minutos
        book->>pay: gRPC Charge (la respuesta tarda)
        book->>bookdb: el job de vencimiento marca Expired y libera el asiento
        pay-->>book: OK con APPROVED y charge_id
        book->>bookdb: UPDATE a Confirmed
        bookdb-->>book: 0 filas, ya no está Retained
        book->>bus: dead-letter con el motivo PaymentApprovedAfterExpiry
        Note over book,pay: log de aviso, no hay reembolso automático
        Note over bus,pay: revisión manual: se busca el cobro por reservation_id en la tabla charges de Payment y, si está Approved, se reembolsa a mano (docs/booking/payment-dlq-review.md)
        Note over avail: el usuario ve Vencida
    end
```

Notas:

- **Camino 1** es el conflicto de la consistencia eventual: las dos solicitudes recibieron "solicitud recibida", y la base de Booking decide un solo ganador.
- **Camino 3** tiene un presupuesto de tiempo validado al arrancar: `3 × deadline + 2 × espera` debe ser menor que la mitad de la retención.
- **Camino 4** es un caso límite: la retención venció durante los intentos de cobro. Booking nunca confirma una reserva cuyo asiento ya se liberó. La reserva se informa como `Expired`.
- Los resultados que no se pueden resolver automáticamente (`ABORTED` agotado, `INVALID_ARGUMENT`, respuesta inesperada de Payment) terminan también en dead-letter y siguen el procedimiento de `docs/booking/payment-dlq-review.md`.

## Fuente

- `src/Availability/Availability.Web/Controllers/` (`AccountController`, `EventsController`, `ReservationRequestsController`, `ReservationController`, `ReservationStatusController`) y `Views/ReservationStatus/Index.cshtml`.
- `src/Availability/Availability.Application/ReservationRequests/ReservationRequestHandler.cs` y `ReservationStatusHandler.cs`.
- `src/Availability/Availability.Infrastructure/Messaging/AzureServiceBusMessagePublisher.cs` y `Booking/BookingStatusClient.cs`.
- `src/Booking/Booking.Api/Messaging/` (`ReservationRequestMessageProcessor`, `ReservationRequestMessageHandler`, `ReservationRequestSettlementDecider`).
- `src/Booking/Booking.Business/Reservations/ReservationRequestProcessor.cs` y `Payments/ReservationPaymentProcessor.cs`.
- `src/Booking/Booking.Api/Payments/GrpcPaymentChargeClient.cs` y `src/Payment/Payment.Grpc/Services/PaymentGrpcService.cs`.
- `docs/contracts/` (`reservation-request.md`, `reservation-status.md`, `occupied-seats.md`, `payment.md`).
- `docs/booking/consumer.md`, `docs/booking/payment-client.md`, `docs/booking/payment-dlq-review.md`.
- `docs/14-autenticacion-usuarios-availability.md` y `docs/11-Autenticacion-entre-servicios.md`.
- `docs/24-logs-de-auditoria.md`: solo existe en la rama `24-audit-logs-correlation-id` (PR abierto), no en `develop`.
