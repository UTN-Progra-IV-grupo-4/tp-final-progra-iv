# T18 - Borrador validado de defensa oral, F01 a F06

> Material personal de exposición validado por Alejandro el 2026-10-31. Explica únicamente el avance de F01 a F06 y no presenta F05/F06 como integración final cerrada.

## 1. Presentación breve

El ticket #18 implementa el procesamiento asincrónico de solicitudes de reserva en Booking. El problema principal es que varias instancias pueden recibir solicitudes simultáneas para el mismo asiento.

La solución separa el transporte, el caso de uso y la persistencia:

- `Booking.Api` adapta Azure Service Bus.
- `Booking.Business` decide el resultado funcional.
- `Booking.Data` persiste y clasifica conflictos.
- PostgreSQL es la autoridad de concurrencia compartida por las réplicas.

Los duplicados se controlan mediante `RequestId`, las competencias por asiento mediante un índice único parcial y el mensaje se completa solamente después de persistir un resultado. Los errores determinísticos van a dead-letter y las excepciones se propagan para permitir reintentos.

F03 a F06 están implementadas y verificadas localmente con 48 pruebas. El equipo definió una retención de 10 minutos. La integración continúa abierta porque faltan el contrato definitivo de #15 y una prueba controlada con Azure Service Bus real.

## 2. Problema que resuelve

Una solicitud representa la reserva de un único asiento para un evento. Booking debe garantizar que:

- un asiento no quede retenido simultáneamente por dos solicitudes;
- una entrega repetida no genere otra reserva;
- varias réplicas puedan procesar mensajes sin compartir memoria;
- el mensaje no se complete antes de guardar el resultado;
- un error transitorio pueda reintentarse;
- un mensaje inválido no se reprocese indefinidamente.

La implementación distingue dos situaciones:

1. El mismo mensaje fue entregado nuevamente: idempotencia por `RequestId`.
2. Dos solicitudes diferentes compiten por el mismo asiento: concurrencia resuelta por PostgreSQL.

## 3. Arquitectura por capas

```text
Azure Service Bus
        │
        ▼
Booking.Api
Consumer, mapper, settlement y logs
        │
        ▼
Booking.Business
Validación y reglas del caso de uso
        │
        ▼
Booking.Data
Persistencia y clasificación de conflictos
        │
        ▼
PostgreSQL
Autoridad de concurrencia
```

### Booking.Api

Contiene la conexión, el ciclo de vida del consumer, la adaptación del mensaje, el settlement y los logs. Configura `AutoCompleteMessages = false` para controlar explícitamente cuándo se completa el mensaje.

Evidencia: `src/Booking/Booking.Api/Messaging/AzureServiceBusReservationRequestConsumer.cs`.

### Booking.Business

No conoce Azure Service Bus. Recibe `RequestId`, `EventId` y `SeatId` mediante `ProcessReservationRequest` y devuelve uno de estos resultados:

- `Retained`;
- `Duplicate`;
- `SeatTaken`;
- `RequestConflict`;
- `InvalidRequest`.

Evidencia: `src/Booking/Booking.Business/Reservations/ReservationRequestProcessor.cs`.

### Booking.Data

Intenta persistir y clasifica la operación como `Created`, `RequestIdExists` o `SeatTaken`. No decide el settlement ni depende del broker.

Evidencia: `src/Booking/Booking.Data/Repositories/ReservationRepository.cs`.

## 4. Flujo de procesamiento

### 4.1 Recepción

Cada réplica crea un `ServiceBusProcessor` sobre el topic y la subscription configurados. Al compartir la subscription se aplica el patrón competing consumers.

```text
Topic
  └── Subscription compartida
         ├── Booking réplica A
         ├── Booking réplica B
         └── Booking réplica C
```

La estructura está implementada localmente, pero todavía no se demostró con varias instancias conectadas al broker real.

### 4.2 Adaptación y mapeo

El consumer extrae body, `MessageId`, `CorrelationId`, `Subject` y `DeliveryCount`. El mapper provisorio valida:

- JSON válido;
- RequestId, EventId y SeatId no vacíos;
- `MessageId` presente y con formato GUID;
- coincidencia entre `MessageId` y RequestId.

Luego crea el comando estable de Business. Evidencia: `src/Booking/Booking.Api/Messaging/ReservationRequestMapper.cs`.

### 4.3 Scope por mensaje

Cada mensaje válido crea un scope de inyección de dependencias y resuelve allí `IReservationRequestProcessor`. Esto evita compartir un `DbContext` entre mensajes concurrentes.

Evidencia: `src/Booking/Booking.Api/Messaging/ReservationRequestMessageHandler.cs`.

### 4.4 Decisión de Business

Business intenta crear una reserva:

```text
Id        = RequestId
EventId   = EventId recibido
SeatId    = SeatId recibido
Status    = Retained
ExpiresAt = UtcNow + 00:10:00
```

El identificador no se genera nuevamente en Booking.

### 4.5 Persistencia

Si PostgreSQL devuelve una violación única `23505`, Data:

1. separa del `DbContext` las entidades cuya escritura falló;
2. busca una reserva con el mismo RequestId;
3. si existe, devuelve `RequestIdExists`;
4. si no existe y chocó el índice del asiento, devuelve `SeatTaken`;
5. si es otra restricción, propaga la excepción.

### 4.6 Resultado funcional

| Resultado Data | Resultado Business |
|---|---|
| `Created` | `Retained` |
| `RequestIdExists` con mismo evento y asiento | `Duplicate` |
| `RequestIdExists` con payload diferente | `RequestConflict` |
| `SeatTaken` | Persistir `Rejected/SeatTaken` |

### 4.7 Settlement

| Resultado | Acción local validada |
|---|---|
| `Retained` | `Complete` |
| `Duplicate` | `Complete` |
| `SeatTaken` | `Complete` |
| Error determinístico del mapper | `DeadLetter` |
| `RequestConflict` | `DeadLetter` |
| `InvalidRequest` | `DeadLetter` |
| Excepción de Business/Data | Sin settlement; propagar |
| Falla de `Complete` o `DeadLetter` | Propagar sin intentar otra acción |

Evidencia: `src/Booking/Booking.Api/Messaging/ReservationRequestSettlementDecider.cs` y `ReservationRequestMessageProcessor.cs`.

## 5. Concurrencia

No se utilizan locks en memoria porque solo protegerían una instancia. PostgreSQL coordina todas las réplicas mediante el índice único parcial sobre `(EventId, SeatId)` para estados `Retained` o `Confirmed`.

Cuando dos solicitudes compiten:

1. ambas intentan insertar `Retained`;
2. una confirma primero;
3. la otra recibe `23505`;
4. Data la clasifica como `SeatTaken`;
5. Business persiste la solicitud perdedora como `Rejected/SeatTaken`.

La prueba `ReservationRequestProcessorIntegrationTests.ProcessAsync_WhenRequestsCompete_StoresRetainedAndRejectedSeatTaken` ejecuta la competencia contra PostgreSQL 16 y verifica una reserva `Retained` y otra `Rejected/SeatTaken`.

## 6. Idempotencia

`Reservation.Id` reutiliza el RequestId recibido y es clave primaria.

```text
Primera entrega: RequestId A → se persiste
Reentrega:       RequestId A → colisión de PK → consultar existente
```

Si RequestId, EventId y SeatId coinciden, el resultado es `Duplicate` y se completa el mensaje. Si el RequestId ya está asociado a otro evento o asiento, el resultado es `RequestConflict` y el mensaje va a dead-letter.

## 7. Por qué SeatTaken se completa

`SeatTaken` no es un fallo técnico. La solicitud fue procesada correctamente y se persistió el resultado funcional `Rejected/SeatTaken`. Reintentar el mismo mensaje no cambiaría ese resultado, por lo que corresponde `Complete`.

## 8. Reintentos y errores transitorios

Las excepciones de Business, Data o settlement:

1. se registran con metadatos seguros;
2. no producen una segunda acción de settlement;
3. se propagan al processor.

Al quedar el mensaje sin settlement exitoso, Azure Service Bus puede volver a entregarlo según su política. Todavía no se definieron valores finales de concurrencia, prefetch, renovación de lock ni máximo de entregas; permanecen bajo `T18-G02`.

## 9. Dead-letter

Se utiliza para errores determinísticos que no se corregirían reintentando el mismo mensaje:

- JSON inválido;
- identificadores faltantes;
- `MessageId` ausente o inválido;
- diferencia entre RequestId y `MessageId`;
- conflicto de payload;
- entrada inválida para Business.

Los motivos son códigos estables y las descripciones no incluyen el body ni secretos.

## 10. Observabilidad

Los logs permiten seguir el procesamiento mediante:

- `MessageId`;
- RequestId después de un mapeo válido;
- `CorrelationId`;
- `DeliveryCount`;
- resultado funcional;
- acción de settlement;
- código de error;
- tipo de excepción.

No se registran body, connection strings, claves, tokens, propiedades completas ni excepciones crudas. Las pruebas de `ReservationRequestMessageProcessorTests` comprueban que los identificadores aparezcan y que el body de prueba no se filtre.

## 11. Evidencia actual

La suite Booking.Tests tiene 48 pruebas correctas, 0 fallidas y 0 omitidas. Cubre:

- persistencia contra PostgreSQL 16 mediante Testcontainers;
- inserción libre y RequestId existente;
- competencia simultánea y recuperación del `DbContext`;
- validaciones, idempotencia y conflicto de identidad;
- cálculo UTC de `ExpiresAt` con tiempo fijo;
- mapper, configuración, ciclo de vida y scope por mensaje;
- decisiones de settlement;
- propagación de excepciones;
- logs seguros.

La solución compiló con 0 errores y 0 advertencias. No se abrió la subscription compartida.

## 12. Pendientes obligatorios

### Contrato definitivo de #15

Todavía no existe en `develop`. Por eso el modelo `T18-G01` continúa siendo provisorio y aún no se conocen clase, namespace, JSON, casing, ubicación o generación del correlation id ni representación final del tipo de mensaje.

### Duración de retención

El equipo definió 10 minutos. Booking lo configura como `ReservationProcessing:RetentionDuration = 00:10:00` y calcula `ExpiresAt` desde UTC. El intervalo del job de vencimiento de #20 es una decisión independiente.

### Azure Service Bus real

F05 y F06 están implementadas y verificadas localmente, pero falta demostrar recepción real, competing consumers, Complete, DeadLetter, reentrega y comportamiento del lock sobre un recurso controlado.

## 13. Preguntas probables y respuestas breves

### ¿Por qué no usar un lock en memoria?

Porque solo coordinaría una instancia. PostgreSQL es compartido por todas las réplicas y arbitra mediante el índice único.

### ¿Cómo se evita retener dos veces el mismo asiento?

El índice único parcial permite una sola reserva activa. Una escritura gana y la otra se clasifica como `SeatTaken`.

### ¿Por qué el índice es parcial?

Porque solo `Retained` y `Confirmed` deben bloquear el asiento. Una reserva `Rejected` o `Expired` no debe impedir una solicitud posterior.

### ¿Cuál es la diferencia entre concurrencia e idempotencia?

Concurrencia son solicitudes diferentes para el mismo asiento; idempotencia es la reentrega de la misma solicitud. La primera se resuelve por evento/asiento y la segunda por RequestId.

### ¿Qué pasa si llega dos veces el mismo mensaje?

La segunda entrega colisiona con la clave primaria. Si evento y asiento coinciden, se devuelve `Duplicate` sin crear otra fila.

### ¿Qué pasa si reutilizan el RequestId con otro asiento?

Se devuelve `RequestConflict`, no se modifica la reserva existente y el mensaje va a dead-letter.

### ¿Por qué se persiste una reserva rechazada?

Para conservar el resultado de la solicitud y permitir informar que fue rechazada por `SeatTaken`.

### ¿Por qué SeatTaken ejecuta Complete?

Porque es un resultado de negocio válido ya persistido, no un error transitorio.

### ¿Por qué un JSON inválido va a dead-letter?

Porque reintentar exactamente el mismo contenido no lo corregirá.

### ¿Qué pasa si PostgreSQL está temporalmente caído?

La excepción se propaga sin settlement para permitir la reentrega.

### ¿Qué pasa si la base guardó pero la aplicación cayó antes de Complete?

El mensaje puede volver a entregarse y la clave primaria permite reconocerlo como duplicado.

### ¿Por qué `AutoCompleteMessages = false`?

Para completar solamente después de obtener un resultado persistido.

### ¿Por qué se crea un scope por mensaje?

Porque `DbContext` es scoped y no debe compartirse entre procesamientos concurrentes.

### ¿Por qué separar las entidades fallidas del `DbContext`?

Porque EF Core continúa siguiéndolas después de `SaveChanges` fallido y podría reintentar accidentalmente la misma escritura.

### ¿El contrato ya es definitivo?

No. El modelo actual está aislado en Booking.Api y se reemplazará por el contrato de #15 sin cambiar Business ni Data.

### ¿Por qué todavía no se exige correlation id y tipo?

La obligación es oficial, pero #15 aún no definió dónde se transportan ni sus nombres concretos. Validarlos ahora inventaría un contrato paralelo.

### ¿Cuánto dura una retención?

Dura 10 minutos por decisión del equipo. La duración permanece configurable y se calcula desde UTC.

### ¿Cómo se demostró el comportamiento?

Con 48 pruebas, incluidas pruebas contra PostgreSQL real. La interacción con Azure Service Bus real continúa pendiente.

## 14. Cierre sugerido

La implementación local resuelve concurrencia, idempotencia y procesamiento seguro. PostgreSQL garantiza una sola reserva activa por asiento, RequestId garantiza idempotencia y el settlement ocurre después de persistir.

El ticket todavía no está cerrado: falta integrar el contrato oficial de #15 y ejecutar una prueba controlada con Azure Service Bus real.
