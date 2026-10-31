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
