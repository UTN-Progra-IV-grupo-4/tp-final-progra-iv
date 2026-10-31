# T18 - Registro de implementación

Este archivo registra cambios reales. Los planes todavía no ejecutados permanecen en `SPEC.md` o en la hoja de ruta.

## 2026-10-31 - Especificación inicial y contrato de persistencia

### Alcance

- Se aprobó `T18-G01`, contrato privado y provisorio para Booking.Api mientras #15 define el contrato compartido.
- Se documentaron requisitos, reglas, settlement y escenarios de aceptación.
- Se inició `T18-C02`, persistencia concurrente en Booking.Data.

### Archivos agregados

| Archivo | Propósito |
|---|---|
| `Booking.Data/Repositories/IReservationRepository.cs` | Puerto de persistencia usado por Business. |
| `Booking.Data/Repositories/ReservationInsertResult.cs` | Resultados `Created`, `RequestIdExists` y `SeatTaken`. |
| `Booking.Data/Repositories/ReservationRepository.cs` | Inserción y clasificación de violaciones únicas PostgreSQL. |
| `tests/Booking/Booking.Tests/Booking.Tests.csproj` | Proyecto xUnit con Testcontainers PostgreSQL. |
| `tests/Booking/Booking.Tests/GlobalUsings.cs` | Importación global de xUnit. |
| `tests/Booking/Booking.Tests/Data/PostgreSqlFixture.cs` | PostgreSQL 16 aislado y migrado para la suite. |
| `tests/Booking/Booking.Tests/Data/ReservationRepositoryIntegrationTests.cs` | Cuatro escenarios reales de persistencia. |

### Archivos modificados

| Archivo | Cambio |
|---|---|
| `BookingDataServiceCollectionExtensions.cs` | Registro scoped de `IReservationRepository`. |
| `TpFinal.sln` | Incorpora `Booking.Tests`. |
| `docs/tickets/18-booking-consumer-roadmap.md` | Contrato provisorio, estado del entorno y reglas de idempotencia. |
| `docs/tickets/TICKET-ROADMAP-TEMPLATE.md` | Estado `PARCIAL` para validaciones que deben repetirse. |

### Comportamiento implementado

1. El repositorio intenta insertar una reserva.
2. Una escritura correcta devuelve `Created`.
3. Ante PostgreSQL `23505`, separa del contexto las entidades fallidas.
4. Si RequestId ya existe, devuelve `RequestIdExists` con la reserva existente.
5. Si no existe el RequestId y chocó `IX_reservations_event_id_seat_id`, devuelve `SeatTaken`.
6. Una restricción única desconocida no se oculta: la excepción continúa.

### Validaciones

| Validación | Resultado |
|---|---|
| Compilación de Booking.Data | Correcta: 0 advertencias y 0 errores. |
| `git diff --check` | Sin errores. |
| Docker | Engine 29.9.2 y Docker Desktop 4.97.0 detectados por Testcontainers. |
| Prueba PostgreSQL | PostgreSQL 16 iniciado, migrado y eliminado automáticamente. |
| Pruebas de repositorio | 4 correctas, 0 fallidas, 0 omitidas. |
| Prueba concurrente | Un resultado `Created`, un resultado `SeatTaken` y una sola fila `Retained`. |
| Recuperación del contexto | Después del conflicto se insertó `Rejected/SeatTaken` con el mismo contexto. |

### Estado

`VERIFIED` para `T18-F03`: la persistencia se demostró contra PostgreSQL real. El criterio completo del ticket sigue pendiente de Business y del consumer.

### Próximo cambio permitido por la especificación

Implementar `T18-F04` en Booking.Business para convertir automáticamente `SeatTaken` en una reserva `Rejected/SeatTaken` y diferenciar duplicado compatible de RequestId conflictivo.

## 2026-10-31 - Caso de uso de Business

### Alcance

- Se implementó `T18-F04` sin dependencias de Azure Service Bus ni del contrato provisorio.
- Se agregó validación de identificadores, cálculo UTC de `ExpiresAt`, idempotencia y persistencia de `Rejected/SeatTaken`.

### Comportamiento implementado

| Resultado | Comportamiento Business |
|---|---|
| Entrada inválida | Devuelve `InvalidRequest` sin acceder al repositorio. |
| `Created` | Devuelve `Retained`. |
| RequestId existente y payload compatible | Devuelve `Duplicate` y conserva el estado existente. |
| RequestId existente con otro evento o asiento | Devuelve `RequestConflict` sin modificar la fila. |
| `SeatTaken` | Inserta `Rejected/SeatTaken` con `ExpiresAt = null`. |
| Duplicado concurrente durante el rechazo | Devuelve `Duplicate` si la identidad es compatible. |
| Error de infraestructura | Se propaga para que el consumer permita el reintento. |

### Archivos principales

- `Booking.Business/Reservations/ProcessReservationRequest.cs`.
- `Booking.Business/Reservations/IReservationRequestProcessor.cs`.
- `Booking.Business/Reservations/ReservationProcessingOptions.cs`.
- `Booking.Business/Reservations/ReservationProcessingResult.cs`.
- `Booking.Business/Reservations/ReservationRequestProcessor.cs`.
- `Booking.Tests/Business/ReservationRequestProcessorTests.cs`.
- `Booking.Tests/Business/ReservationRequestProcessorIntegrationTests.cs`.

### Evidencia

| Validación | Resultado |
|---|---|
| Suite Booking.Tests | 15 correctas, 0 fallidas, 0 omitidas. |
| Concurrencia Business+PostgreSQL | Una `Retained` y una `Rejected/SeatTaken`. |
| Compilación de la solución | 0 advertencias y 0 errores. |
| Otros servicios | Availability, Payment y Contracts compilan sin modificaciones funcionales. |

### Estado

`VERIFIED` para `T18-F04`. El próximo bloque es `T18-F05`, adaptador de Azure Service Bus con contrato privado provisorio.

## 2026-10-31 - Consumer de Azure Service Bus, F05

### Alcance

- Se implementó la configuración, ciclo de vida, recepción, deserialización, mapeo y delegación de F05 dentro de Booking.Api.
- Se utilizó el contrato privado provisorio `T18-G01`; no se agregó ningún tipo a `src/Contracts`.
- Se mantuvieron como provisorias las decisiones de `T18-G02`.
- No se implementaron todavía las decisiones finales de `Complete`, `DeadLetter`, retry ni observabilidad de F06.

### Comportamiento implementado

1. Booking.Api enlaza `Broker:Provider` y `Broker:AzureServiceBus` desde configuración.
2. Las credenciales, topic y subscription son obligatorias solamente si el consumer se habilita.
3. La configuración versionada deja `Broker:AzureServiceBus:Enabled = false`.
4. El hosted service delega inicio y detención al puerto `IReservationRequestConsumer`.
5. El adaptador usa `Azure.Messaging.ServiceBus` 7.21.0 y procesamiento manual con `AutoCompleteMessages = false`.
6. El mapper valida JSON, identificadores, formato de `MessageId` y coincidencia con `requestId`.
7. Cada mensaje válido crea un scope y resuelve allí `IReservationRequestProcessor`.
8. Business y Data permanecen independientes del SDK del broker.

### Archivos principales

- `Booking.Api/Messaging/BrokerOptions.cs`.
- `Booking.Api/Messaging/IReservationRequestConsumer.cs`.
- `Booking.Api/Messaging/AzureServiceBusReservationRequestConsumer.cs`.
- `Booking.Api/Messaging/ReservationRequestConsumerHostedService.cs`.
- `Booking.Api/Messaging/ProvisionalReservationRequestMessage.cs`.
- `Booking.Api/Messaging/ReservationRequestMapper.cs`.
- `Booking.Api/Messaging/ReservationRequestMessageHandler.cs`.
- `Booking.Tests/Messaging/`.

### Evidencia

| Validación | Resultado |
|---|---|
| Restauración | SDK oficial `Azure.Messaging.ServiceBus` 7.21.0 restaurado. |
| Suite Booking.Tests | 30 correctas, 0 fallidas, 0 omitidas. |
| Mapper provisorio | Mensaje válido, JSON inválido, identificadores vacíos, `MessageId` ausente, inválido o diferente. |
| Delegación | El handler crea un scope y delega el comando estable a Business. |
| Ciclo de vida | Inicio y detención delegados; configuración deshabilitada no abre el broker. |
| Compilación de la solución | 0 advertencias y 0 errores. |
| Seguridad | Sin connection strings ni claves versionadas. |

### Estado

`PARCIAL` para `T18-F05`: la implementación local está verificada, pero el contrato continúa siendo provisorio y no se ejecutó una recepción real. La activación contra Azure Service Bus queda bloqueada hasta completar F06.

### Próximo cambio permitido por la especificación

Implementar `T18-F06`: relacionar cada resultado de mapper y Business con `Complete`, `DeadLetter` o reintento, y agregar observabilidad local con `RequestId` y `CorrelationId` sin registrar secretos.

## 2026-10-31 - Settlement y observabilidad local, F06

### Alcance

- Se registró y aplicó la matriz de settlement validada por Alejandro.
- Se implementaron decisiones de `Complete` y `DeadLetter` independientes del SDK para permitir pruebas sin Azure.
- Las excepciones de procesamiento y settlement se registran con metadatos seguros y se propagan para reintento.
- El consumer continúa deshabilitado y no se abrió ninguna conexión al broker compartido.

### Comportamiento implementado

| Resultado | Acción |
|---|---|
| `Retained`, `Duplicate`, `SeatTaken` | `Complete`. |
| Error determinístico del mapper | `DeadLetter` con el mismo código. |
| `RequestConflict` | `DeadLetter` con `RequestPayloadConflict`. |
| `InvalidRequest` | `DeadLetter` con su código o fallback `InvalidRequest`. |
| Excepción de Business/Data | Sin settlement; log seguro y propagación. |
| Falla de `Complete` o `DeadLetter` | Sin acción alternativa; log seguro y propagación. |

### Diseño

- `ReservationRequestSettlementDecider` transforma resultados en una decisión pura y testeable.
- `ReservationRequestMessageProcessor` coordina handler, settlement y logs.
- `IReservationRequestMessageSettlement` desacopla las pruebas del SDK.
- `AzureServiceBusReservationRequestMessageSettlement` adapta `ProcessMessageEventArgs`.
- Los logs incluyen MessageId, RequestId cuando existe, CorrelationId, DeliveryCount, outcome, error code, acción y tipo de excepción.
- No se registran body, connection string, tokens, propiedades completas ni excepciones crudas.

### Evidencia

| Validación | Resultado |
|---|---|
| Suite Booking.Tests | 48 correctas, 0 fallidas, 0 omitidas. |
| Outcomes exitosos | `Retained`, `Duplicate` y `SeatTaken` resuelven `Complete`. |
| Mapper | Sus siete códigos determinísticos resuelven `DeadLetter`. |
| Business inválido | `RequestConflict` e `InvalidRequest` resuelven `DeadLetter`. |
| Reintento | Las excepciones se propagan sin otra acción de settlement. |
| Logs | RequestId y CorrelationId presentes cuando corresponde; body y excepción cruda ausentes. |
| Compilación | Solución completa con 0 advertencias y 0 errores. |
| Formato | Booking.Api y Booking.Tests sin cambios requeridos. |
| Broker | Sin conexión; `Enabled` continúa en `false`. |

### Estado

`PARCIAL` para `T18-F06`: la matriz y el comportamiento están verificados localmente. Falta una prueba controlada contra Azure Service Bus después de autorizar la activación.

### Próximo cambio permitido por la especificación

Ejecutar la revisión de F07 y preparar una prueba controlada que demuestre el flujo completo sin poner en riesgo mensajes de la subscription compartida.

## 2026-10-31 - Auditoría de avance, especificaciones y protocolo

### Alcance

- Se contrastaron `SPEC.md`, la hoja de ruta y este registro con el código, las pruebas y los commits reales de F03 a F06.
- Se verificó que la documentación continúa siendo un registro personal y que `T18-G01`, `T18-G02` y `T18-B02` permanecen abiertos.
- No se modificó código productivo, pruebas ni configuración y no se inició F07.

### Resultado

| Área | Resultado de la auditoría |
|---|---|
| F03 y F04 | Implementación y evidencia completas; estado `VALIDADO`. |
| F05 | Implementado y verificado localmente; permanece `PARCIAL` por el contrato provisorio y la falta de recepción real controlada. |
| F06 | Implementado y verificado localmente; permanece `PARCIAL` hasta una prueba controlada con Azure Service Bus. |
| F07 | Existe evidencia local reutilizable, pero la fase de aceptación y el smoke test no comenzaron; estado documental `PENDIENTE`. |
| F08 | La documentación personal está avanzada, pero faltan la guía final de ejecución y la preparación de defensa; estado `PARCIAL`. |
| F09 | Revisión final contra `develop` y preparación del PR pendientes. |

### Evidencia revalidada

| Validación | Resultado |
|---|---|
| Rama | Limpia y sincronizada con `origin/18-booking-consumir-solicitudes-de-reserva`. |
| Suite Booking.Tests | 48 correctas, 0 fallidas, 0 omitidas. |
| Commits F05 y F06 | Implementación, pruebas y documentación separados. |
| Configuración | Consumer deshabilitado; valores sensibles ausentes de la configuración versionada. |
| Secretos | No se detectaron connection strings ni claves de Azure Service Bus en archivos versionados. |
| Broker | No se abrió la subscription compartida. |

### Estado

`VALIDADO`: Alejandro aprobó el diff documental el 2026-10-31. Las correcciones quedaron limitadas a la especificación personal, el registro de implementación y la hoja de ruta, sin iniciar F07 ni modificar código productivo.

## 2026-10-31 - Borrador de defensa oral de F01 a F06

### Alcance

- Se preparó y validó un guion personal de exposición limitado a F01-F06.
- Se relacionaron arquitectura, flujo, concurrencia, idempotencia, settlement, reintentos, dead-letter y observabilidad con el código y las pruebas actuales.
- Se incorporaron preguntas probables de defensa y respuestas breves.
- Se conservaron como pendientes el contrato de #15, la duración de retención y la prueba real con Azure Service Bus.

### Resultado

- Documento agregado: `ORAL-DEFENSE.md`.
- Estado de F08: `PARCIAL`; existe un borrador validado, pero la documentación final depende de F07 y de las definiciones pendientes.
- Sin cambios en código productivo, pruebas o configuración.

## 2026-10-31 - Duración oficial de retención

### Decisión

- El equipo definió una retención de 10 minutos.
- `T18-B02` queda cerrado.
- La duración continúa siendo configuración y no una constante dentro de Business.
- El intervalo del job de vencimiento de #20 es una decisión independiente.

### Cambios preparados

- `Booking.Api/appsettings.json`: `ReservationProcessing:RetentionDuration = 00:10:00`.
- Pruebas de Business: cálculo esperado de `ExpiresAt` actualizado a 10 minutos desde un UTC fijo.
- Especificación, hoja de ruta y defensa oral alineadas con la decisión.

### Restricciones conservadas

- `T18-G01` y `T18-G02` permanecen abiertos.
- El consumer continúa deshabilitado.
- No se realiza conexión a Azure Service Bus durante esta validación.

### Validaciones

| Validación | Resultado |
|---|---|
| Configuración | `RetentionDuration=00:10:00`. |
| Cálculo UTC | Las pruebas verifican `ExpiresAt = UtcNow + 10 minutos`. |
| Suite Booking.Tests | 48 correctas, 0 fallidas, 0 omitidas. |
| Consumer | `AzureServiceBusEnabled=False`. |
| Broker | Sin conexión a Azure Service Bus. |

### Estado

`VERIFICADO_LOCALMENTE`: la decisión y el comportamiento están comprobados; los cambios permanecen sin commit para revisión separada de implementación, pruebas y documentación.
