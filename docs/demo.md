# Guion de la demo

Guion corto para mostrar el camino feliz y el flujo de fallo. Los pasos para levantar todo están en [running-locally.md](running-locally.md); los diagramas, en [diagrams/happy-path-and-failure.md](diagrams/happy-path-and-failure.md). Tiempo total: unos 10 minutos.

## Antes de empezar (checklist)

- [ ] Docker Desktop encendido y `docker compose up -d postgres-db keycloak` hecho. Keycloak con el realm recién importado (si cambiaste el realm o los secretos: `docker compose down -v` y repetir la carga de la sección 4 de la guía).
- [ ] Migraciones aplicadas y catálogo cargado (2 eventos, 40 asientos).
- [ ] Estado reiniciado: corriste los tres scripts de [Reiniciar el estado entre ensayos](running-locally.md#7-reiniciar-el-estado-entre-ensayos), con los servicios frenados.
- [ ] Suscripción de Azure Service Bus vacía (mensajes activos en 0) y sin consumidores ajenos.
- [ ] Payment, Booking y Availability corriendo en tres terminales visibles, con las variables de la guía (en Availability, `EventQueries__Source=Database` y `DevelopmentAuthentication__Enabled=false`). Esperá a ver `Now listening on` en cada una: `dotnet run` compila y tarda unos 10 a 15 s.
- [ ] Calentamiento: la primera reserva después de arrancar los servicios es más lenta (arranque en frío: hasta unos 6 s en confirmarse, contra 3 s en caliente). Hacé una reserva de prueba en el segundo evento, "Recital de ejemplo" (la demo usa solo la Conferencia), así no hace falta reiniciar el estado.
- [ ] Navegador en `http://localhost:5212` y sin sesión iniciada (probá en una ventana privada).

## Camino feliz (unos 3 minutos)

| Paso | Qué hacer | Qué se ve |
|---|---|---|
| 1 | Abrir `/eventos` y entrar a "Conferencia de tecnología". | Mapa de 20 asientos, todos libres, sin aviso de "Modo demostración". No hace falta login para mirar. |
| 2 | Elegir A1 y tocar "Elegir asiento", luego "Reservar este asiento". | Pantalla "Iniciar Sesión"; "Login con Keycloak" lleva al login de Keycloak (en inglés, "Sign in to seat-reservation"). |
| 3 | Entrar con `user1` / `User123!`. | Vuelve al mapa con A1 seleccionado ("Seleccionaste el asiento A1"). |
| 4 | Tocar "Reservar este asiento". | "Solicitud recibida" con el número de solicitud, en menos de 1 s: todavía no es una reserva (Availability publicó el mensaje). |
| 5 | Tocar "Ver el estado de mi solicitud". | Procesando (o ya Confirmada, si el procesamiento terminó antes) y, en la consulta siguiente, Confirmada: entre 3 y 6 s después de tocar "Reservar" (la página consulta cada 3 s). Retenida dura menos de 1 s y casi nunca se alcanza a ver. |
| 6 | Volver al mapa. | A1 aparece ocupado y no se puede elegir. |

Mientras tanto, en las terminales: Availability registra el evento 1001/1002, Booking el 2001, 2010/2011 y 2002, Payment el 3001/3002. En caliente, de 1001 a 2002 pasa menos de 1 s.

Para repetir el recorrido sin cerrar el navegador, tocar "Logout" (cierra también la sesión de Keycloak).

## Camino de fallo: Payment caído (unos 2 minutos)

1. Detener Payment (Ctrl+C en su terminal).
2. Reservar otro asiento (por ejemplo A2) con el mismo recorrido.
3. En la página de estado: Procesando (o Retenida) y, tras los tres intentos de cobro, **Rechazada: "El servicio de pagos no estaba disponible."** Medido en Windows: unos 11 s entre el pedido y el rechazo en Booking (cada intento tarda unos 4 s en fallar), 12 s en pantalla con la consulta cada 3 s. En las consolas, Booking imprime por cada intento un stack trace de `SocketException` y el warning 2004 "Possible late payment": es esperado, avisalo.
4. El asiento A2 queda libre: la reserva rechazada no lo ocupa.

Alternativa sin detener nada: arrancar Payment con `SimulatedGateway__Mode=Decline`. El resultado es **Rechazada: "El pago fue rechazado."**, en unos 3 s.

Otro fallo, sin tocar servicios: dos usuarios que eligen el mismo asiento a la vez. El mapa no deja elegir un asiento ya confirmado, así que hay que preparar la carrera antes: en una ventana privada, entrar como `user2` / `User123!`, elegir un asiento libre (por ejemplo A3) y dejar la página abierta sin reservar. En la otra ventana, reservar A3 con `user1` hasta Confirmada. De vuelta en la ventana de `user2`, tocar "Reservar este asiento": Procesando y, a los 3 s, **Rechazada: "El asiento ya estaba ocupado."** (Si recargás el mapa antes de reservar, A3 ya aparece ocupado y no se puede elegir.)

Para dejar el sistema como estaba, volver a arrancar Payment.

## Mostrar los logs de auditoría (unos 2 minutos)

Tomar el número de solicitud de la página "Solicitud recibida" y buscarlo en los logs de los tres servicios (cada uno lo imprime como `CorrelationId`; con Azure, el 1002 dice "Azure Service Bus message published to topic ..."). El detalle de eventos y los comandos para filtrar están en [24-logs-de-auditoria.md](24-logs-de-auditoria.md): el recorrido es 1001, 1002 (Availability), 2001, 2010, 2011, 2002 (Booking) y 3001, 3002 (Payment). En el caso de Payment caído, el 2012 de Booking muestra `ErrorCode=Unavailable` por cada intento.

## Dónde mostrar los diagramas (unos 2 minutos)

Después del camino feliz, abrir [diagrams/happy-path-and-failure.md](diagrams/happy-path-and-failure.md): el diagrama (a) coincide con lo que se acaba de ver y los de fallo explican los demás casos. La arquitectura de cada servicio está en [diagrams/](diagrams/README.md).

## Ruido esperado en las consolas

- Availability ya no imprime el SQL de cada consulta (`Microsoft.EntityFrameworkCore.Database.Command` en `Warning`). Si necesitás verlo para depurar, subí ese nivel a `Information` con `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information`.
- Una advertencia `Failed to determine the https port for redirect` en Availability y Booking, en el primer pedido (corren solo por HTTP).
- Con Payment caído, Booking ya no imprime los stack traces de `SocketException` (`Grpc.Net.Client` en `Critical`). Siguen los eventos de auditoría (2011/2012) y la advertencia de posible cobro tardío (2004), que es esperada.

## Limitaciones (decirlas en voz alta)

- Las notificaciones no están implementadas: el profesor confirmó que no hacen falta.
- El login con Google está configurado en Keycloak pero no se probó a través de Availability; la demo usa los usuarios del realm.
- Todo corre por HTTP en local, no HTTPS (`RequireHttpsMetadata=false` solo para desarrollo).
- El cobro es simulado (`SimulatedGateway`), no hay una pasarela real.
- El flujo completo necesita la suscripción de Azure Service Bus de la cátedra. Sin ella, Availability solo registra el mensaje y Booking no lo recibe: se ve "Solicitud recibida" pero no hay confirmación.
- Las reservas confirmadas no se liberan: entre ensayos hay que reiniciar el estado.
