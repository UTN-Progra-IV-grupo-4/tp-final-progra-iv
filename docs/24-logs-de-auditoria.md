# Logs de auditoría y correlation id (#24)

Cada servicio registra el inicio y el fin de su tramo de la cadena con el mismo identificador, el **correlation id**. Filtrar los logs de los tres servicios por ese valor muestra el recorrido completo de una solicitud.

## Decisión: librería de logging

Se mantiene `Microsoft.Extensions.Logging` (la abstracción de ASP.NET Core) con logs estructurados y *scopes*. No se agrega ningún paquete.

- Los tres servicios ya la usan y el proveedor de consola cubre la demo.
- La plantilla del mensaje (`{CorrelationId}`) conserva cada campo como propiedad: un proveedor estructurado (Serilog, Application Insights, OpenTelemetry) se puede enchufar después sin tocar el código, solo el cableado de `Program.cs`.
- Los eventos de auditoría tienen un `EventId` numérico fijo (ver tabla). La consola lo muestra como `Categoría[id]`, así que se filtra por el número y no por el texto del mensaje.

Cuándo reconsiderarlo: si hace falta centralizar los logs de varias instancias o consultarlos por campo, el cambio es agregar un proveedor, no reescribir los logs.

## Cómo viaja el correlation id

| Tramo | Medio | Nombre | Quién lo genera o lee |
| --- | --- | --- | --- |
| Usuario a Availability | HTTP | (no lo manda) | Availability genera el id de la solicitud (`Guid`) y lo usa como `CorrelationId` |
| Availability a Booking (mensaje) | Broker | `correlationId` del mensaje (y `MessageId` del broker) | Booking lo lee, lo valida y lo registra |
| Availability a Booking (consulta de estado) | HTTP | cabecera `x-correlation-id` con el id de la solicitud | Booking la lee en un middleware |
| Booking a Payment | gRPC | metadata `x-correlation-id` | Payment la lee y la pone en un scope |

El nombre de la cabecera y la regla de saneamiento viven en un solo lugar: `Contracts.CorrelationIdPolicy`.

La consulta de **ocupación de asientos** (mapa público de asientos) no manda correlation id a propósito: no pertenece a ninguna reserva, así que no hay cadena que seguir, y un id inventado por cada visita solo sumaría ruido.

### Saneamiento

El valor llega de afuera y termina en cada línea de log. Booking (cabecera HTTP) y Payment (metadata gRPC) aplican la misma regla:

- Hasta 64 caracteres, solo letras ASCII, dígitos y `- _ . :`.
- Si no cumple (más largo, espacios, saltos de línea, caracteres de control, varios valores), se reemplaza por `invalido`. No se corta, porque un valor cortado parece válido pero ya no coincide con el del emisor.
- Nunca se rechaza el pedido por esto. Sin cabecera, el log muestra `CorrelationId:(null)`.
- Booking devuelve la cabecera `x-correlation-id` en la respuesta solo si el valor era válido.
- Para el mensaje del broker, Booking ya rechazaba (dead-letter) un `correlationId` vacío o inseguro; eso no cambia.

La autenticación de Booking no cambia: el middleware corre antes y solo abre el scope de logging, de modo que también quedan asociados al id los pedidos rechazados con 401.

## Eventos de auditoría

La hora UTC la imprime el formateador de consola (`TimestampFormat` y `UseUtcTimestamp` en `appsettings.json` de los tres servicios). Nunca se registra el cuerpo del mensaje, el id del usuario, el monto ni credenciales o tokens.

### Availability (1xxx)

| Id | Evento | Cuándo | Campos |
| --- | --- | --- | --- |
| 1001 | `ReservationRequestStored` | Inicio de la cadena: la solicitud pasó la validación y se guardó | `Service`, `RequestId`, `CorrelationId`, `EventId`, `SeatId` |
| 1002 | `ReservationRequestPublished` | El broker aceptó el mensaje (sin broker configurado lo emite `LoggingMessagePublisher` con otro texto) | `Service`, `MessageId`, `CorrelationId` |
| 1003 | `ReservationRequestPublishFailed` | El broker rechazó el mensaje y la solicitud guardada se eliminó (nivel Error) | `Service`, `RequestId`, `CorrelationId`, `ErrorCode=PublishFailed` |

El estado consultado por el usuario no genera eventos de auditoría (se consulta cada pocos segundos). Cuando Booking no responde ya se registra un warning con el `RequestId`, que es el correlation id.

### Booking (2xxx)

| Id | Evento | Cuándo | Campos |
| --- | --- | --- | --- |
| 2001 | `ReservationMessageReceived` | Inicio en Booking: llegó el mensaje | `Service`, `MessageId`, `CorrelationId`, `DeliveryCount` |
| 2010 | `PaymentChargeRequested` | Se envía un intento de cobro a Payment (uno por reintento) | `Service`, `RequestId`, `CorrelationId` |
| 2011 | `PaymentChargeAnswered` | Payment respondió | `Service`, `RequestId`, `CorrelationId`, `ChargeId`, `PaymentOutcome` (`Approved`/`Declined`) |
| 2012 | `PaymentChargeFailed` | El intento falló (nivel Warning) | `Service`, `RequestId`, `CorrelationId`, `ErrorCode` (`Unavailable`, `DeadlineExceeded`, `Pending`, `InvalidRequest`, `UnexpectedResponse`) |
| 2004 | `PossibleLatePayment` | Resultado de cobro desconocido tras los reintentos (#19, nivel Warning) | `Service`, `ErrorCode=PaymentOutcomeUnknown`, `MessageId`, `RequestId`, `CorrelationId`, `ChargeId` y `ReservationStatus` (o `PaymentFailure` si no hubo respuesta), `SettlementAction`, `OccurredAtUtc`, `DeliveryCount` |
| 2005 | `LateApprovedPayment` | Cobro aprobado después de que la reserva dejó de estar retenida (#19, nivel Warning) | `Service`, `ErrorCode` de aprobación tardía, `MessageId`, `RequestId`, `CorrelationId`, `ChargeId`, `ReservationStatus`, `SettlementAction`, `OccurredAtUtc`, `DeliveryCount` |
| 2002 | `ReservationMessageCompleted` | Fin en Booking: mensaje completado | `Service`, `MessageId`, `RequestId`, `CorrelationId`, `ReservationStatus`, `Result`, `ChargeId`, `SettlementAction` |
| 2003 | `ReservationMessageDeadLettered` | Fin en Booking: mensaje a la dead-letter queue (nivel Warning) | `Service`, `MessageId`, `RequestId`, `CorrelationId`, `ReservationStatus`, `ChargeId`, `SettlementAction`, `ErrorCode` |
| 2006 | `ReservationMessageFailed` | El procesamiento o el settlement falló y el broker va a reentregar | `MessageId`, `CorrelationId`, `ExceptionType` (sin mensaje de la excepción) |
| 2020 | `HttpRequestReceived` | Llegó un pedido HTTP (nivel **Debug**, apagado por defecto) | scope `Service` y `CorrelationId` |

En 2002, `ReservationStatus` es el estado en que termina la reserva (`Confirmed`, `Rejected`, `Expired`, `Retained`; vacío si no se puede saber sin leer la base, por ejemplo una entrega duplicada) y `Result` es lo que lo decidió: el resultado del pago si hubo cobro (`Confirmed`, `PaymentFailed`, `PaymentUnavailable`, `Expired`...) o el del procesamiento (`SeatTaken`, `Retained`, `Duplicate`). Antes el log decía `Outcome: Retained; PaymentOutcome: PaymentFailed` para una reserva que terminaba rechazada.

Las reservas que vencen por el temporizador (`ExpiredReservationsBackgroundService`) se registran por cantidad, no una por una, y sin correlation id: Booking no lo guarda en la reserva. Es una limitación conocida.

Para ver los pedidos HTTP en Booking (2020): `Logging__LogLevel__Booking.Api.Logging=Debug`.

### Payment (3xxx)

Los textos de Payment están en español, como el resto de ese servicio. `Service` y `CorrelationId` salen del scope que abre `PaymentGrpcService` antes de validar el pedido.

| Id | Evento | Cuándo | Campos |
| --- | --- | --- | --- |
| 3001 | `ChargeStarted` | Inicio en Payment: llegó un pedido de cobro (los reintentos también) | `ReservationId` (+ scope) |
| 3002 | `ChargeFinished` | Fin en Payment: la pasarela respondió y el resultado se guardó | `ReservationId`, `ChargeId`, `Estado` (+ scope) |
| 3003 | `ChargeReplayed` | Fin de un reintento: se responde con el cobro guardado, sin llamar a la pasarela | `ReservationId`, `ChargeId`, `Estado` (+ scope) |
| 3004 | `ChargeRejected` | Fin con error (nivel Warning) | `ErrorCode` (código gRPC: `InvalidArgument`, `Unavailable`, `Aborted`, `Internal`), `ExceptionType` si hubo excepción (+ scope) |

## Ruido que se bajó para la demo

En `appsettings.json` de Booking:

| Categoría | Nivel | Por qué |
| --- | --- | --- |
| `Microsoft.EntityFrameworkCore.Database.Command` | `Critical` | Imprimía cada SQL en Development y, ante una violación del índice único, un `fail:` con stack trace |
| `Microsoft.EntityFrameworkCore.Update` | `Critical` | Segundo `fail:` con stack trace por el mismo `SeatTaken` |
| `System.Net.Http.HttpClient` | `Warning` | Unas diez líneas por cada cobro (cliente gRPC y pedido de token) |

`System.Net.Http.HttpClient` también está en `Warning` en Availability.

`SeatTaken` es un resultado esperado (dos usuarios eligen el mismo asiento y el índice único decide), no una falla. Lo que se pierde: el stack trace que EF imprimía ante una falla real de base de datos. Una falla real igual aparece: el procesamiento relanza la excepción y Booking registra el evento 2006 con el tipo de excepción, y el broker reentrega el mensaje. Los demás componentes de `Microsoft.EntityFrameworkCore` siguen con el nivel por defecto. Si hace falta el detalle para depurar, se vuelve a subir con `Logging__LogLevel__Microsoft.EntityFrameworkCore.Update=Error`.

`IncludeScopes` está activo en Booking (como en Payment) para que se vean el `Service` y el `CorrelationId` de los pedidos HTTP.

## Seguir un pedido en los tres servicios

1. Tomar el `requestId` de la respuesta de `POST /api/reservations` (o del evento 1001 en el log de Availability).
2. Buscarlo en los logs de los tres servicios. La consola imprime cada entrada en dos o tres líneas (encabezado con hora e id de evento, scopes y mensaje), por eso conviene pedir líneas de contexto.

```powershell
$requestId = "<requestId>"

# Si cada servicio escribe a un archivo (dotnet run ... > availability.log)
Select-String -Path availability.log, booking.log, payment.log -Pattern $requestId -Context 2,0

# Si corren en contenedores (ajustar los nombres de los contenedores)
foreach ($container in "availability", "booking", "payment") {
    docker logs $container 2>&1 | Select-String -Pattern $requestId -Context 2,0
}

# Solo los eventos de inicio y fin
Select-String -Path availability.log, booking.log, payment.log -Pattern "\[(1001|2001|2002|2003|3001|3002|3003|3004)\]"
```

Recorrido esperado de una reserva aprobada (ids de evento y hora UTC; los valores son de una corrida real con los ids acortados):

```text
14:07:23.512Z  Availability  [1001] Reservation request stored. Service: Availability; RequestId: 15f62cbc-...; CorrelationId: 15f62cbc-...
14:07:23.512Z  Availability  [1002] Reservation event logged without a broker. ... CorrelationId: 15f62cbc-...
14:07:35.194Z  Booking       [2001] Reservation message received. Service: Booking; ... CorrelationId: 15f62cbc-...
14:07:35.947Z  Booking       [2010] Payment charge requested. ... CorrelationId: 15f62cbc-...
14:07:36.687Z  Payment       [3001] => Service:Payment CorrelationId:15f62cbc-...  /  Inicio de cobro. ReservationId=15f62cbc-...
14:07:37.400Z  Payment       [3002] => Service:Payment CorrelationId:15f62cbc-...  /  Fin de cobro. ... Estado=Approved
14:07:37.418Z  Booking       [2011] Payment charge answered. ... PaymentOutcome: Approved.
14:07:37.445Z  Booking       [2002] Reservation message completed. ... ReservationStatus: Confirmed; Result: Confirmed; SettlementAction: Complete.
14:08:30.102Z  Booking       [2020] => Service:Booking CorrelationId:15f62cbc-...  /  HTTP request received (consulta de estado de Availability)
```

## Qué se probó

- Pruebas con un logger que captura entradas y scopes: los eventos llevan `Service` y `CorrelationId`, no filtran el cuerpo ni credenciales, el estado final de 2002 es el correcto, y la cabecera de Booking se acepta, se reemplaza o se omite según la regla (válida, demasiado larga, con saltos de línea o caracteres de control, ausente).
- `BookingStatusClient` manda `x-correlation-id` con el id de la solicitud; la consulta de ocupación no manda ninguno.
- Corrida local de los tres servicios con Keycloak y PostgreSQL reales (sin Azure Service Bus: el mensaje se entregó a Booking con el procesador real, sin pasar por el broker).
