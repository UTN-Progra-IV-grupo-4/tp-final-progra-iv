# T18 - Booking consume solicitudes de reserva

> Especificación de trabajo personal para el desarrollo asignado a Alejandro. El issue #18 y las decisiones del equipo continúan siendo las fuentes oficiales.

## 1. Identidad

| Campo | Valor |
|---|---|
| Issue | [#18 - Booking: consumir solicitudes de reserva](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/18) |
| Responsable | Alejandro Galarza |
| Estado SDD | `IMPLEMENTING` con contrato de entrada `PROVISORIO` |
| Componentes | Booking.Api, Booking.Business y Booking.Data |
| Arquitectura | N-Layer, dominio anémico |

Hoja de ruta relacionada: [`docs/tickets/18-booking-consumer-roadmap.md`](../../../tickets/18-booking-consumer-roadmap.md).

### Clasificación de las definiciones

Esta especificación personal distingue el origen y el grado de cierre de cada decisión:

| Estado | Uso |
|---|---|
| `VALIDADO` | Está definido por el TP, un issue, una decisión cerrada del equipo o una validación expresa del responsable. |
| `CURSO-VALIDADO` | Tiene fundamento en las clases 01 a 12 y fue validado expresamente por el responsable antes de incorporarlo. |
| `PROVISORIO` | No está cerrado por el TP, los tickets o el equipo. Debe permanecer aislado e indicar su condición de reemplazo. |

El material de la cursada permite justificar conceptos y decisiones de diseño, pero no convierte en definitivos los detalles concretos del SDK de Azure Service Bus.

## 2. Objetivo observable

Una réplica de Booking consume una solicitud de un solo asiento. Si el asiento está libre, persiste una reserva `Retained`; si otra reserva activa ya ocupa el asiento, persiste `Rejected/SeatTaken`. El mismo mensaje puede entregarse más de una vez sin crear reservas adicionales.

## 3. Fuera de alcance

- Publicar el mensaje desde Availability: #15.
- Cobrar y confirmar la reserva: #19 y #22.
- Definir o transportar cantidad, monto o moneda: #13, #15, #19 y #22 confirman que no pertenecen a la solicitud consumida por #18.
- Vencer una retención: #20.
- Exponer el estado por HTTP: #21.
- Implementar la auditoría distribuida completa: #24.

## 4. Requisitos

| ID | Requisito | Estado | Evidencia prevista |
|---|---|---|---|
| `T18-RQ-01` | Consumir mensajes desde el topic y subscription configurados. | Pendiente | Prueba controlada con Azure Service Bus. |
| `T18-RQ-02` | Permitir múltiples réplicas mediante competing consumers. | Pendiente | Dos procesos sobre la misma subscription. |
| `T18-RQ-03` | Crear `Retained` cuando el asiento esté libre. | Verificado en Business y Data | Prueba de integración PostgreSQL aprobada. |
| `T18-RQ-04` | Persistir `Rejected/SeatTaken` cuando el asiento esté ocupado. | Verificado en Business y Data | Prueba concurrente Business+PostgreSQL aprobada. |
| `T18-RQ-05` | Tratar una reentrega compatible como duplicado exitoso. | Verificado en Business | Prueba unitaria de idempotencia aprobada. |
| `T18-RQ-06` | Rechazar el mismo RequestId con otro evento o asiento. | Verificado localmente en Business y settlement | Pruebas unitarias de conflicto y dead-letter aprobadas. |
| `T18-RQ-07` | Calcular `ExpiresAt` en UTC con una duración configurable. | Implementación verificada; valor final bloqueado | Prueba con TimeProvider fijo aprobada. |
| `T18-RQ-08` | Completar el mensaje solamente después de persistir el resultado. | Verificado localmente | Pruebas unitarias de settlement aprobadas; falta prueba real. |
| `T18-RQ-09` | Reintentar errores transitorios y enviar mensajes inválidos a dead-letter. | Verificado localmente | Pruebas de propagación y dead-letter aprobadas; falta prueba real. |

## 5. Reglas e invariantes

### T18-BR-01 - PostgreSQL arbitra el asiento

El índice único parcial `IX_reservations_event_id_seat_id` impide dos reservas `Retained` o `Confirmed` para el mismo evento y asiento. No se utilizan locks en memoria como garantía de concurrencia.

### T18-BR-02 - RequestId es la identidad

`Reservation.Id` es el RequestId generado por Availability y no se genera nuevamente en Booking.

### T18-BR-03 - Duplicado compatible

Una reentrega es idempotente solamente si RequestId, EventId y SeatId coinciden con la reserva existente.

### T18-BR-04 - Conflicto de identidad

Si RequestId ya existe pero EventId o SeatId son diferentes, el mensaje se considera inconsistente. No modifica la reserva existente y debe ir a dead-letter.

### T18-BR-05 - Persistir antes de completar

Booking ejecuta `Complete` solamente después de guardar `Retained`, `Rejected/SeatTaken` o de comprobar un duplicado compatible.

### T18-BR-06 - Contexto recuperable

Después de una violación única `23505`, las entidades fallidas dejan de estar tracked antes de consultar o realizar otra escritura con el mismo contexto.

### T18-BR-07 - Réplicas sin coordinación en memoria

Las réplicas procesan solicitudes de forma independiente. No comparten locks en memoria ni estado estático; PostgreSQL, el índice único y la idempotencia arbitran los accesos concurrentes.

Estado: `CURSO-VALIDADO`. Fundamento: réplicas y base compartida en C02-P16; concurrencia y condiciones de carrera en C03-P10 y C03-P11.

### T18-BR-08 - Trazabilidad local

El procesamiento registra inicio, resultado y error con `RequestId` y, cuando esté disponible, `CorrelationId`. Los logs no deben incluir connection strings ni otros secretos.

Estado: `CURSO-VALIDADO`. Fundamento: logging, metadatos y propagación de identificadores en C09-P04, C09-P05 y C09-P06. La auditoría distribuida completa permanece fuera de alcance y pertenece a #24.

### T18-BR-09 - Un asiento por solicitud

Cada mensaje representa exactamente un asiento. Lleva la identidad de la solicitud, el evento y el asiento; no incluye cantidad, monto ni moneda. El precio fijo se incorpora posteriormente en Booking al ejecutar #19 y #22.

Estado: `VALIDADO` por las definiciones actuales de #13, #15, #18, #19 y #22.

## 6. Contrato de entrada: reglas oficiales y representación provisoria

La revisión del 2026-10-31 distingue las reglas ya cerradas por los tickets de la forma concreta todavía pendiente del contrato de #15.

| Regla oficial | Fuente |
|---|---|
| Una solicitud representa un solo asiento. | #13, #15, #18, #19 y #22. |
| El mensaje comunica evento y asiento, sin cantidad, monto ni moneda. | #15, #19 y #22. |
| `MessageId` es igual al id de solicitud. | #15. |
| El mensaje debe transportar un correlation id y un tipo de mensaje. | #15. |

Representación de desarrollo: `PROVISORIA`, identificada como `T18-G01`.

El siguiente JSON es solamente la forma candidata usada por Booking.Api; los nombres, casing y presencia de `requestId` en el body no están definidos todavía por #15:

```json
{
  "requestId": "00000000-0000-0000-0000-000000000000",
  "eventId": "00000000-0000-0000-0000-000000000000",
  "seatId": "00000000-0000-0000-0000-000000000000"
}
```

Metadata y estado:

| Dato | Regla oficial | Detalle todavía provisorio |
|---|---|---|
| `MessageId` | Igual al id de solicitud. | Formato concreto y si `requestId` también aparece en el body. |
| `CorrelationId` | Debe estar presente y Booking lo conserva. | Ubicación, formato y regla de generación. |
| Tipo de mensaje | Debe estar presente. | Uso de `Subject` u otra propiedad y nombre exacto. |

El modelo vive solamente en `Booking.Api`. No se agrega a `src/Contracts`. Cuando #15 publique el contrato definitivo se reemplazarán el tipo de entrada y el mapper, sin modificar Business ni Data.

Continúan provisorios el formato JSON, clase, namespace, nombres, casing, `Subject` o mecanismo equivalente, ubicación o generación del correlation id y campos adicionales. Como base de desarrollo se rechazan JSON inválidos, identificadores vacíos y diferencias entre `MessageId` y `requestId`; la validación del tipo exacto queda pendiente del contrato de #15.

Aunque la presencia de correlation id y tipo ya es una regla oficial, `T18-G01` todavía no rechaza su ausencia porque #15 no definió dónde ni con qué nombre se transportan. Esa validación se agrega al reemplazar el contrato, sin inventar una ubicación provisoria.

## 7. Diseño por capas

Estado: `CURSO-VALIDADO` por el responsable el 2026-10-31.

Fundamento: separación lógica N-Layer en C02-P09 y C06-P11; adaptadores y dependencias hacia las reglas interiores en C06-P13. Este fundamento valida la ubicación de responsabilidades, no los tipos concretos utilizados por el SDK.

```text
Azure Service Bus
        ↓
Booking.Api / mensaje y mapper
        ↓
Booking.Business / caso de uso estable
        ↓
Booking.Data / persistencia y clasificación de restricciones
        ↓
PostgreSQL / autoridad de concurrencia
```

## 8. Resultados de persistencia

| Resultado Data | Significado | Decisión posterior |
|---|---|---|
| `Created` | La fila fue insertada. | Continuar y completar cuando corresponda. |
| `RequestIdExists` | Ya existe una fila con el mismo RequestId. | Business compara EventId y SeatId. |
| `SeatTaken` | Chocó el índice parcial del asiento y RequestId no existe. | Persistir `Rejected/SeatTaken`. |

## 9. Settlement

Estado: matriz `VALIDADA` por el responsable el 2026-10-31. Las llamadas concretas al SDK continúan bajo `T18-G02` hasta verificarlas localmente en F06.

La regla funcional es persistir antes de confirmar y propagar las excepciones de procesamiento. Con `AutoCompleteMessages = false`, una excepción no asentada queda disponible para el mecanismo de reintento del processor.

| Origen | Resultado | Acción validada |
|---|---|---|
| Mapper | Cualquier error determinístico de `ReservationRequestMappingErrorCodes` | `DeadLetter` con el mismo código y una descripción genérica. |
| Business | `Retained`, `Duplicate` o `SeatTaken` | `Complete`. |
| Business | `RequestConflict` | `DeadLetter` con `RequestPayloadConflict`. |
| Business | `InvalidRequest` | `DeadLetter` con su `ErrorCode` o `InvalidRequest`. |
| Business/Data | Excepción o cancelación | No ejecutar settlement; registrar metadatos seguros y propagar para reintento. |
| SDK | Falla `Complete` o `DeadLetter` | No intentar una segunda acción; registrar el tipo de excepción y propagar. |

Logs permitidos: `MessageId`, `RequestId` después de un mapeo válido, `CorrelationId`, `DeliveryCount`, outcome, error code, acción de settlement y tipo de excepción. No se registran body, connection string, claves, tokens, propiedades completas ni mensajes crudos de excepción. Booking conserva un `CorrelationId` ausente como `null`; no genera uno mientras #15 siga pendiente.

Pruebas locales requeridas sin broker:

1. Cada error determinístico del mapper produce un único `DeadLetter` y no invoca Business.
2. `Retained`, `Duplicate` y `SeatTaken` producen un único `Complete`.
3. `RequestConflict` e `InvalidRequest` producen un único `DeadLetter` con código estable.
4. Una excepción de procesamiento o settlement se propaga y no dispara otra acción.
5. Los logs contienen RequestId y CorrelationId cuando corresponden y nunca el body o secretos.
6. El consumer permanece deshabilitado y ninguna prueba abre una conexión a Azure Service Bus.

### T18-G02 - Diseño técnico provisorio de F05 y F06

Las siguientes decisiones permiten construir y probar el adaptador sin presentarlas como definiciones del TP, la cursada o el equipo:

| Área | Definición provisoria | Condición de cierre o reemplazo |
|---|---|---|
| Ciclo de vida | Ejecutar el consumer como `IHostedService` o `BackgroundService` dentro de `Booking.Api`. | Verificar inicio, detención y manejo de cancelación; registrar la alternativa finalmente implementada. |
| Settlement | Configurar procesamiento manual, inicialmente con `AutoCompleteMessages = false`. | Validar en F06 cada ruta de `Complete`, `DeadLetter` y reintento. |
| Scope | Crear un scope de dependencias por mensaje y resolver allí Business y Data; no compartir un `DbContext` entre mensajes. | Verificar mediante pruebas concurrentes y revisión de lifetimes. |
| Configuración | Leer `Broker:Provider`, `Broker:AzureServiceBus:ConnectionString`, `Broker:AzureServiceBus:TopicName` y `Broker:AzureServiceBus:SubscriptionName`. La duración continúa bajo opciones de procesamiento de reservas. | Ajustar si el equipo define otra estructura; connection string únicamente en User Secrets o variables de entorno. |
| Mapper | Deserializar `T18-G01`, validar JSON e identificadores y mapear al comando interno estable. | Reemplazar modelo y mapper cuando #15 publique el contrato definitivo. |
| Ajustes del SDK | No fijar todavía valores definitivos de concurrencia, prefetch, renovación de lock ni límites de reintento. | Definirlos con evidencia de pruebas o decisión del equipo. |
| Prueba compartida | No consumir la subscription compartida en una prueba manual sin autorización explícita y control del mensaje utilizado. | Ejecutar un smoke test coordinado o utilizar un recurso aislado. |

Límite de fases:

- F05 configura el adaptador, recibe, deserializa, mapea y delega a Business.
- F06 cierra las decisiones de settlement, retries, dead-letter y observabilidad local.
- El consumer permanece deshabilitado hasta una prueba controlada y autorizada; completar F06 localmente no autoriza por sí solo el consumo de la subscription compartida.

## 10. Mapa especificación-código

| Requisitos | Archivo | Cambio | Estado |
|---|---|---|---|
| `T18-RQ-03..06` | `src/Booking/Booking.Data/Repositories/IReservationRepository.cs` | Agregado | Compila; falta integración. |
| `T18-RQ-03..06` | `src/Booking/Booking.Data/Repositories/ReservationInsertResult.cs` | Agregado | Compila; falta integración. |
| `T18-RQ-03..06` | `src/Booking/Booking.Data/Repositories/ReservationRepository.cs` | Agregado | Compila; falta integración. |
| `T18-RQ-03..06` | `src/Booking/Booking.Data/BookingDataServiceCollectionExtensions.cs` | Modificado | Compila. |
| `T18-RQ-03..05` | `tests/Booking/Booking.Tests/Data/PostgreSqlFixture.cs` | Agregado | Verificado con PostgreSQL 16. |
| `T18-RQ-03..05` | `tests/Booking/Booking.Tests/Data/ReservationRepositoryIntegrationTests.cs` | Agregado | 4/4 pruebas aprobadas. |
| `T18-RQ-03..05` | `tests/Booking/Booking.Tests/Booking.Tests.csproj` | Agregado | Incluido en la solución. |
| `T18-RQ-03..07` | `src/Booking/Booking.Business/Reservations/ProcessReservationRequest.cs` | Agregado | Verificado. |
| `T18-RQ-03..07` | `src/Booking/Booking.Business/Reservations/ReservationRequestProcessor.cs` | Agregado | Verificado. |
| `T18-RQ-03..07` | `src/Booking/Booking.Business/Reservations/ReservationProcessingResult.cs` | Agregado | Verificado. |
| `T18-RQ-07` | `src/Booking/Booking.Business/Reservations/ReservationProcessingOptions.cs` | Agregado | Verificado con valor de prueba. |
| `T18-RQ-03..07` | `tests/Booking/Booking.Tests/Business/ReservationRequestProcessorTests.cs` | Agregado | 14 casos unitarios aprobados. |
| `T18-RQ-03,04` | `tests/Booking/Booking.Tests/Business/ReservationRequestProcessorIntegrationTests.cs` | Agregado | Concurrencia real aprobada. |
| `T18-RQ-01,02` | `src/Booking/Booking.Api/Messaging/` | Agregado | Recepción, mapeo y delegación verificados localmente; conexión real pendiente. |
| `T18-RQ-08,09` | `src/Booking/Booking.Api/Messaging/` | Agregado | Settlement y observabilidad verificados localmente; prueba real pendiente. |
| `T18-RQ-01,02,08,09` | `tests/Booking/Booking.Tests/Messaging/` | Agregado | Mapper, scope, configuración, settlement, propagación de excepciones y logs seguros verificados. |

## 11. Escenarios de aceptación

| ID | Given | When | Then |
|---|---|---|---|
| `T18-AC-01` | No existe reserva activa para el asiento. | Se procesa una solicitud válida. | Queda una reserva `Retained` con el RequestId recibido. |
| `T18-AC-02` | Ya se procesó la misma solicitud. | El broker la entrega otra vez. | No se agrega otra fila y el mensaje se completa. |
| `T18-AC-03` | RequestId existe para otro asiento. | Llega un payload conflictivo. | No cambia la reserva y el mensaje va a dead-letter. |
| `T18-AC-04` | El asiento está libre. | Dos RequestId compiten simultáneamente. | Una reserva queda `Retained` y la otra `Rejected/SeatTaken`. |
| `T18-AC-05` | PostgreSQL falla de forma transitoria. | Se procesa el mensaje. | El mensaje no se completa y puede reintentarse. |

## 12. Pendientes

| ID | Pendiente | Impacto | Condición de cierre |
|---|---|---|---|
| `T18-G01` | Clase y documentación del contrato definitivo de #15 todavía ausentes de `develop`. | Impide cerrar la integración aunque las reglas semánticas básicas ya estén definidas. | Reemplazar modelo y mapper provisorios y validar con un mensaje producido desde el contrato compartido. |
| `T18-G02` | Detalles técnicos del consumer y settlement. | Permite avanzar, pero no debe presentarse como decisión del equipo. | Implementar, probar y registrar la alternativa final; mantener provisorio lo que siga sin acuerdo. |
| `T18-B02` | Duración de retención todavía “A definir” en #18. | Impide calcular el `ExpiresAt` final. | Acordar valor y configurarlo sin hardcodearlo. |
| `T18-E01` | Docker/PostgreSQL para pruebas. | Cerrado: Docker Desktop y Testcontainers operativos. | Cumplida el 2026-10-31. |

## 13. Evidencia actual

- `dotnet test tests/Booking/Booking.Tests/Booking.Tests.csproj --no-restore /p:UseSharedCompilation=false`.
- Resultado: 4 pruebas correctas, 0 fallidas y 0 omitidas contra PostgreSQL 16.
- Casos: inserción libre, RequestId existente, competencia simultánea y escritura de `Rejected/SeatTaken` después del conflicto.
- `dotnet test tests/Booking/Booking.Tests/Booking.Tests.csproj /p:UseSharedCompilation=false`.
- Resultado acumulado después de F04: 15 pruebas correctas, 0 fallidas y 0 omitidas.
- La prueba Business+PostgreSQL dejó exactamente una fila `Retained` y una `Rejected/SeatTaken`.
- `dotnet build TpFinal.sln --no-restore /p:UseSharedCompilation=false`: 0 advertencias y 0 errores.
- Resultado acumulado después de F05: 30 pruebas correctas, 0 fallidas y 0 omitidas.
- F05 agregó pruebas del contrato `T18-G01`, coherencia de `MessageId`, delegación al processor scoped, validación de opciones y ciclo de vida con el consumer deshabilitado.
- Resultado acumulado después de F06: 48 pruebas correctas, 0 fallidas y 0 omitidas.
- F06 verificó todos los errores del mapper, outcomes de Business, propagación de excepciones, fallos de settlement y ausencia de body o excepción cruda en logs.
- No se abrió una conexión a Azure Service Bus ni se consumió la subscription compartida.
