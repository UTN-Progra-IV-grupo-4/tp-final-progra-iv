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

The test used local PostgreSQL 16, `grupo4-booking-topic/booking`, two Booking.Api processes on ports 5088 and 5089, and the sender from `tests/Booking/Booking.BrokerSmoke`. It ran on a working tree based on `347ff96` that already used the shared contract but was not yet committed; the later commits of this branch only add tests and documentation, so the consumer code path is the same. It was run by the PR author and has not been independently reproduced.

| Instance | MessageId / RequestId | Consumer log | PostgreSQL result |
| --- | --- | --- | --- |
| A (`5088`) | `259dc2ed-60fa-4aba-86ee-b0f8eaf5ee6f` | `DeliveryCount: 1`, `Outcome: SeatTaken`, completed | `Rejected`, `SeatTaken` |
| B (`5089`) | `32ff266a-9db8-41fb-b6cc-8402601d1bd0` | `DeliveryCount: 1`, `Outcome: Retained`, completed | `Retained`, expiry `2026-11-04 03:45:05 UTC` |

Both rows had `EventId=08bddb69-f132-412d-b5d2-2ef37e665f33` and `SeatId=554b9a94-fef4-4fa2-8e55-2e4d3037be10`. The active subscription was empty before and after this run. Two pre-existing dead-letter messages with different IDs remained untouched. Both Booking processes were stopped, and the two local reservation rows from this run were deleted after the result was recorded. No credentials, user details, or message bodies are included in this record.

This demonstrates two competing Booking consumers and one seat winner. The real Availability-to-Service-Bus publisher path remains a separate integration check when #15 publishes to Azure Service Bus.

## Reproducción del 2026-11-05

Segunda ejecución de la prueba, con más volumen y con el flujo completo (cobro real). Quién la ejecutó: una sesión automatizada de un agente (Claude Code), dirigida por el dueño del repositorio. Es un ejecutor distinto del autor original de la prueba del 2026-11-04. Nada de esto reemplaza una revisión humana de los resultados.

### Entorno

- Una sola máquina Windows con Docker Desktop: PostgreSQL 16 (compose del repositorio, puerto 55432) y Keycloak 26.0.7 con el realm `seat-reservation`.
- `Payment.Grpc` real (`Payment__Enabled=true` en ambas réplicas) y dos procesos `Booking.Api` (puertos 5088 y 5089) sobre la misma base `booking` y la misma subscription `grupo4-booking-topic/booking`. Cada proceso escribe a su propio archivo de log; la instancia que atendió un mensaje se identifica por el archivo donde aparece el evento `[2001] Reservation message received`.
- Código: `origin/develop` (`82e8783`) con la rama del ticket #24 fusionada (eventos de auditoría con id).

### Pasos

1. `docker compose up -d postgres-db keycloak` con variables `POSTGRES_PORT`, `KEYCLOAK_*` y las contraseñas de las bases definidas con valores descartables en un archivo fuera del repositorio.
2. Migraciones de Booking y Payment: `dotnet ef database update --project src/Booking/Booking.Data --startup-project src/Booking/Booking.Api` y `--project src/Payment/Payment.Data --startup-project src/Payment/Payment.Grpc`, con `ConnectionStrings__Booking` y `ConnectionStrings__Payment` en el entorno. Tablas `reservations` y `charges` vacías al empezar.
3. Un `Payment.Grpc` con `ConnectionStrings__Payment`, `Authentication__Authority` y `Authentication__RequireHttpsMetadata=false`.
4. Dos `Booking.Api` con las mismas variables, distintos `ASPNETCORE_URLS`: `ConnectionStrings__Booking`, `Authentication__Authority`, `Authentication__RequireHttpsMetadata=false`, `Payment__Enabled=true`, `Payment__Address`, `ServiceCredentials__Authority|ClientId|ClientSecret` (client `booking-service`), `Broker__Provider=AzureServiceBus`, `Broker__AzureServiceBus__Enabled|TopicName|SubscriptionName|ConnectionString`. Esperar a que ambos logueen `Azure Service Bus reservation consumer started`.
5. Antes y después: `PeekMessages` de la subscription (solo identificar, sin consumir): 0 activos y 2 mensajes viejos en la dead-letter queue, que no son de esta prueba.
6. Publicar 23 mensajes en el topic con el SDK de Service Bus (`MessageId` = `CorrelationId` = `messageId` del cuerpo JSON camelCase de `Contracts.ReservationRequestMessage`), todos a la vez sobre los 20 asientos del evento `...0002`:
   - 17 mensajes con `MessageId` distinto y asiento distinto;
   - 2 asientos disputados, cada uno con 2 `MessageId` distintos (4 mensajes);
   - 1 mensaje duplicado: el mismo `MessageId` enviado 2 veces (2 mensajes).
7. Contar los eventos `[2001]` y `[2002]` de cada log, consultar `reservations` (Booking) y `charges` (Payment) y repetir el `PeekMessages`.
8. Detener los procesos por PID, `docker compose down -v` y borrar solo los mensajes propios si hubiera quedado alguno (no quedó ninguno).

### Reparto entre instancias

Se recibieron 23 entregas en total (todas con `DeliveryCount: 1`), entre las 14:46:15Z y las 14:46:21Z; el último mensaje se completó 0,3 s después.

| Instancia | Entregas | Mensajes distintos | Conflictos | Duplicado |
| --- | --- | --- | --- | --- |
| A (`5088`) | 11 | 7: `161c42f6`, `108acdb6`, `94345761`, `bdd4662a`, `f91f8b67`, `dda127d7`, `e974b943` | 2 ganadores (`88591b7d`, `57b1df96`) | las 2 copias de `a4a674b6` |
| B (`5089`) | 12 | 10: `2d898a1e`, `e8cbd499`, `564d778a`, `587b6f16`, `74769a48`, `b32656cf`, `88c818d8`, `a49bea04`, `e573f3d2`, `0aa726d0` | 2 perdedores (`360a1679`, `a92b7b1a`) | ninguna |

### Estados persistidos

| Caso | Resultado en `reservations` | Resultado en el log (`[2002]`) |
| --- | --- | --- |
| 17 asientos sin disputa | 17 `Confirmed` | `Result: Confirmed` |
| Asiento disputado 1 (`88591b7d` en A, `360a1679` en B) | `Confirmed` y `Rejected/SeatTaken` | `Confirmed` y `SeatTaken` |
| Asiento disputado 2 (`57b1df96` en A, `a92b7b1a` en B) | `Confirmed` y `Rejected/SeatTaken` | `Confirmed` y `SeatTaken` |
| `MessageId` duplicado (`a4a674b6`) | 1 sola fila `Confirmed` | primera copia `Confirmed`, segunda `AlreadyTerminal` |

Totales: 22 filas en `reservations` (22 `id` distintos), 20 `Confirmed` y 2 `Rejected/SeatTaken`; 20 filas en `charges`, todas `Approved`, una por reserva confirmada y ninguna por las rechazadas ni por la copia duplicada. Después de la prueba la subscription tenía 0 mensajes activos y la dead-letter queue seguía con los mismos 2 mensajes anteriores (no se creó ninguno).

### Conclusión

Con dos réplicas sobre la misma subscription y la misma base, cada mensaje se procesó exactamente una vez en total, el reparto fue entre las dos instancias, el índice único decidió un ganador por asiento disputado (en ambos casos el ganador y el perdedor los atendieron procesos distintos, lo que da la evidencia cruzada que pedía el ticket) y el reintento del mismo `MessageId` no generó una segunda reserva ni un segundo cobro.

### Qué no demuestra

- Una sola máquina y una sola subscription, en una ventana corta (unos 6 segundos de tráfico): no prueba el comportamiento con más réplicas, con carga sostenida ni con latencia de red real.
- Las dos copias del `MessageId` duplicado llegaron a la misma instancia (A): la idempotencia se comprobó, pero no con copias repartidas entre procesos. La subscription no tiene detección de duplicados del lado del broker (las dos copias se entregaron).
- El reparto concreto (11 y 12) lo decide Service Bus y puede ser distinto en otra corrida; no es una propiedad que se pueda exigir.
- No cubre fallas durante el procesamiento (caída de una réplica con un mensaje en vuelo, expiración del lock) ni la lógica de reintentos del cobro.
- Lo ejecutó un agente automatizado y no fue revisado de forma independiente por otra persona.

Este registro no incluye cadenas de conexión, secretos, cuerpos de mensajes ni datos de usuario; los identificadores están abreviados a 8 caracteres.
