# Guion de la demo

Guion corto para mostrar el camino feliz y el flujo de fallo. Los pasos para levantar todo están en [running-locally.md](running-locally.md); los diagramas, en [diagrams/happy-path-and-failure.md](diagrams/happy-path-and-failure.md). Tiempo total: unos 10 minutos.

## Antes de empezar (checklist)

- [ ] Docker Desktop encendido y `docker compose up -d postgres-db keycloak` hecho. Keycloak con el realm recién importado (si cambiaste el realm o los secretos: `docker compose down -v` y repetir la carga de la sección 4 de la guía).
- [ ] Migraciones aplicadas y catálogo cargado (2 eventos, 40 asientos).
- [ ] Estado reiniciado: corriste los tres scripts de [Reiniciar el estado entre ensayos](running-locally.md#7-reiniciar-el-estado-entre-ensayos), con los servicios frenados.
- [ ] Suscripción de Azure Service Bus vacía (mensajes activos en 0) y sin consumidores ajenos.
- [ ] Payment, Booking y Availability corriendo en tres terminales visibles, con las variables de la guía (en Availability, `EventQueries__Source=Database` y `DevelopmentAuthentication__Enabled=false`).
- [ ] Navegador en `http://localhost:5212` y sin sesión iniciada (probá en una ventana privada).

## Camino feliz (unos 3 minutos)

| Paso | Qué hacer | Qué se ve |
|---|---|---|
| 1 | Abrir `/eventos` y entrar a "Conferencia de tecnología". | Mapa de 20 asientos, todos libres, sin aviso de "Modo demostración". No hace falta login para mirar. |
| 2 | Elegir A1 y tocar "Elegir asiento", luego "Reservar este asiento". | Pantalla "Iniciar Sesión"; "Login con Keycloak" lleva al login de Keycloak. |
| 3 | Entrar con `user1` / `User123!`. | Vuelve al mapa con A1 seleccionado. |
| 4 | Tocar "Reservar este asiento". | "Solicitud recibida" con el número de solicitud: todavía no es una reserva (Availability publicó el mensaje). |
| 5 | Tocar "Ver el estado de mi solicitud". | Procesando, luego Retenida y a los pocos segundos Confirmada (la página consulta cada 3 s). |
| 6 | Volver al mapa. | A1 aparece ocupado y no se puede elegir. |

Mientras tanto, en las terminales: Availability registra el evento 1001/1002, Booking el 2001 y 2010/2011, Payment el 3001/3002.

## Camino de fallo: Payment caído (unos 2 minutos)

1. Detener Payment (Ctrl+C en su terminal).
2. Reservar otro asiento (por ejemplo A2) con el mismo recorrido.
3. En la página de estado: Procesando, y tras los tres intentos de cobro (hasta unos 17 segundos) **Rechazada: "El servicio de pagos no estaba disponible."**
4. El asiento A2 queda libre: la reserva rechazada no lo ocupa.

Alternativa sin detener nada: arrancar Payment con `SimulatedGateway__Mode=Decline`. El resultado es **Rechazada: "El pago fue rechazado."**

Otro fallo, sin tocar servicios: con otra sesión (`user2` / `User123!`) reservar el mismo asiento ya confirmado. Queda Rechazada con "El asiento ya estaba ocupado."

Para dejar el sistema como estaba, volver a arrancar Payment.

## Mostrar los logs de auditoría (unos 2 minutos)

Tomar el número de solicitud de la página "Solicitud recibida" y buscarlo en los logs de los tres servicios (cada uno lo imprime como `CorrelationId`). El detalle de eventos y los comandos para filtrar están en [24-logs-de-auditoria.md](24-logs-de-auditoria.md): el recorrido es 1001, 1002 (Availability), 2001, 2010, 2011, 2002 (Booking) y 3001, 3002 (Payment). En el caso de Payment caído, el 2012 de Booking muestra `ErrorCode=Unavailable` por cada intento.

## Dónde mostrar los diagramas (unos 2 minutos)

Después del camino feliz, abrir [diagrams/happy-path-and-failure.md](diagrams/happy-path-and-failure.md): el diagrama (a) coincide con lo que se acaba de ver y los de fallo explican los demás casos. La arquitectura de cada servicio está en [diagrams/](diagrams/README.md).

## Limitaciones (decirlas en voz alta)

- Las notificaciones no están implementadas: el profesor confirmó que no hacen falta.
- El login con Google está configurado en Keycloak pero no se probó a través de Availability; la demo usa los usuarios del realm.
- Todo corre por HTTP en local, no HTTPS (`RequireHttpsMetadata=false` solo para desarrollo).
- El cobro es simulado (`SimulatedGateway`), no hay una pasarela real.
- El flujo completo necesita la suscripción de Azure Service Bus de la cátedra. Sin ella, Availability solo registra el mensaje y Booking no lo recibe: se ve "Solicitud recibida" pero no hay confirmación.
- Las reservas confirmadas no se liberan: entre ensayos hay que reiniciar el estado.
