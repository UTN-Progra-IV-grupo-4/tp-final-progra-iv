# T18 - Booking consume solicitudes de reserva

> Especificación de trabajo personal para el desarrollo asignado a Alejandro. El issue #18 y las decisiones del equipo continúan siendo las fuentes oficiales.

## 1. Identidad

| Campo | Valor |
|---|---|
| Issue | [#18 - Booking: consumir solicitudes de reserva](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/18) |
| Responsable | Alejandro Galarza |
| Estado SDD | `IMPLEMENTING` con contrato de entrada `PROVISIONAL` |
| Componentes | Booking.Api, Booking.Business y Booking.Data |
| Arquitectura | N-Layer, dominio anémico |

Hoja de ruta relacionada: [`docs/tickets/18-booking-consumer-roadmap.md`](../../../tickets/18-booking-consumer-roadmap.md).

## 2. Objetivo observable

Una réplica de Booking consume una solicitud de un solo asiento. Si el asiento está libre, persiste una reserva `Retained`; si otra reserva activa ya ocupa el asiento, persiste `Rejected/SeatTaken`. El mismo mensaje puede entregarse más de una vez sin crear reservas adicionales.

## 3. Fuera de alcance

- Publicar el mensaje desde Availability: #15.
- Cobrar y confirmar la reserva: #19 y #22.
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
| `T18-RQ-06` | Rechazar el mismo RequestId con otro evento o asiento. | Business verificado; settlement pendiente | Pruebas unitarias de conflicto aprobadas. |
| `T18-RQ-07` | Calcular `ExpiresAt` en UTC con una duración configurable. | Implementación verificada; valor final bloqueado | Prueba con TimeProvider fijo aprobada. |
| `T18-RQ-08` | Completar el mensaje solamente después de persistir el resultado. | Pendiente | Prueba de settlement. |
| `T18-RQ-09` | Reintentar errores transitorios y enviar mensajes inválidos a dead-letter. | Pendiente | Pruebas de settlement. |

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

## 6. Contrato de entrada provisorio

Estado: `PROVISIONAL`, identificado como `T18-G01`.

```json
{
  "requestId": "00000000-0000-0000-0000-000000000000",
  "eventId": "00000000-0000-0000-0000-000000000000",
  "seatId": "00000000-0000-0000-0000-000000000000"
}
```

Metadata provisional:

| Dato | Regla |
|---|---|
| `MessageId` | Igual a RequestId. |
| `CorrelationId` | Se conserva para logs y propagación. |
| `Subject` | Nombre exacto pendiente de #15. |

El modelo vive solamente en `Booking.Api`. No se agrega a `src/Contracts`. Cuando #15 publique el contrato definitivo se reemplazarán el tipo de entrada y el mapper, sin modificar Business ni Data.

## 7. Diseño por capas

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

| Resultado | Acción prevista |
|---|---|
| Reserva creada | `Complete`. |
| Duplicado compatible | `Complete`. |
| `SeatTaken` persistido | `Complete`. |
| RequestId conflictivo | `DeadLetter`. |
| JSON o identificadores inválidos | `DeadLetter`. |
| Error transitorio de infraestructura | No completar; permitir reintento. |

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
| `T18-RQ-01,02,08,09` | `src/Booking/Booking.Api/Messaging/` | Agregar | Pendiente. |
| Todos | `tests/Booking/Booking.Tests/` | Agregar | Pendiente. |

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
| `T18-G01` | Contrato definitivo de #15. | Impide cerrar la integración. | Reemplazar mensaje provisorio y validar con mensaje real. |
| `T18-B02` | Duración de retención. | Impide calcular el `ExpiresAt` final. | Acordar valor y configurarlo. |
| `T18-E01` | Docker/PostgreSQL para pruebas. | Cerrado: Docker Desktop y Testcontainers operativos. | Cumplida el 2026-10-31. |

## 13. Evidencia actual

- `dotnet test tests/Booking/Booking.Tests/Booking.Tests.csproj --no-restore /p:UseSharedCompilation=false`.
- Resultado: 4 pruebas correctas, 0 fallidas y 0 omitidas contra PostgreSQL 16.
- Casos: inserción libre, RequestId existente, competencia simultánea y escritura de `Rejected/SeatTaken` después del conflicto.
- `dotnet test tests/Booking/Booking.Tests/Booking.Tests.csproj /p:UseSharedCompilation=false`.
- Resultado acumulado después de F04: 15 pruebas correctas, 0 fallidas y 0 omitidas.
- La prueba Business+PostgreSQL dejó exactamente una fila `Retained` y una `Rejected/SeatTaken`.
- `dotnet build TpFinal.sln --no-restore /p:UseSharedCompilation=false`: 0 advertencias y 0 errores.
