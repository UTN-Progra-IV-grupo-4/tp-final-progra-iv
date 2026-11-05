# Historial de prompts — Ezequiel Shinboku Tokashiki Etcheverry

**Componentes y tickets:** Booking: estructura base y base de datos (#17) y Booking: vencimiento de reservas retenidas (#20)
**Herramienta de IA usada:** Agente Copilot de Visual Studio Code / github
**Período:** 24/10/2026 al 05/11/2026.
**Alcance:** prompts que orientaron decisiones, revisiones y la organización del trabajo. Es un registro individual del uso de IA, no una metodología aprobada por el equipo.



## Cómo leer el registro

- se marcan las prompts dentro de los «»
- Las citas son mensajes reales míos, agrupados en orden segun su rama y commit. Solo registro los prompts, no las respuestas del agente.
- Para que la lista sea corta y legible, corregí tipeos y ortografía, y recorté muletillas y dudas que no sumaban. No cambié el sentido de ningún pedido ni agregué pedidos que no hice. Un `…` marca un recorte.
- Omití confirmaciones breves, mensajes repetidos y consultas cuyo foco era el análisis general del repositorio o de las ramas.


## Proceso del issue #17

**Chore: Building Data and Business Libraries**«Aplica las siguientes modificaciones al repositorio
Crear Booking.Business y Booking.Data como bibliotecas net10.0, 
agregarlas a TpFinal.sln y establecer referencias entre proyectos según la aceptación: Booking.Api → Booking.Business → Booking.Data. 
Ningún proyecto debería referenciar directamente a otro servicio.»

**Chore: Reservation Definition** «Aplica la siguiente modificacion;
definir Reservation y sus estados: Retenida, Confirmada, Rechazada y Vencida. 
Añadir los motivos de rechazo: asiento ocupado, pago fallido y pago no disponible.»

**Chore: configure PostgreSQL persistence with EF Core** «(primera query del commit)
Aplica la siguiente modificacion;
agrega PostgreSQL y EF Core a Booking.Data: tambien se configuro Npgsql, el DbContext y el mapeo de la entidad.»

**segunda query del commit**«Aplica la siguiente modificacion;
Configurar la conexión sin guardar secretos en Git. El .gitignore ya excluye .env; también se pueden usar dotnet user-secrets. En cambio, la configuración actual de Booking.Api.csproj y appsettings.json todavía no incluye la persistencia.»

**chore: first migration config**«Crear la migración inicial. Según las notas del issue, generarla no requiere tener PostgreSQL levantado. Aplicarla sí requiere una base accesible: el Compose correspondiente a #3 aún está abierto.»

**Chore: Updated Issue Documentation**«Aplica la siguiente modificacion;
Documentar brevemente las decisiones de capas y patrones que no sean evidentes, como pide el issue, y verificar la solución con dotnet build TpFinal.sln.»



## Prompts usadas en el Issue #20 segun commit

**Chore(Booking): Implement reservation expiration persistence** «Aplica la siguiente modificacion;
En Booking.Data, implementá la consulta y actualización de reservas vencidas. Seleccioná reservas Retained con ExpiresAt <= now y actualizalas a Expired mediante una operación atómica que vuelva a comprobar ambas condiciones.»

**Chore(Booking): Add retained reservation expiration use case** «Aplica la siguiente modificacion;
En Booking.Business, implementá el caso de uso ExpireRetainedReservations utilizando el repositorio. Debe ejecutar la expiración y devolver la cantidad de reservas procesadas, sus IDs y el timestamp UTC de ejecución.»

**Chore(Booking): Implement expired reservations background worker** «Aplica la siguiente modificacion;
En Booking.Api, implementá ExpiredReservationsBackgroundService utilizando BackgroundService y PeriodicTimer. Ejecutá el caso de uso en cada intervalo, registrá las expiraciones mediante ILogger y manejá excepciones sin interrumpir futuras ejecuciones. Registrá el worker en Program.cs.»

**Chore(Booking): Configure reservation processing intervals** «Aplica la siguiente modificacion;
Configurá ReservationProcessing con RetentionDuration = 00:10:00 y ExpirationCheckInterval = 00:01:00. Utilizá una clase de configuración tipada, registrala en DI y conectala al caso de uso y al worker. No utilices valores hardcodeados.»

**Chore(Booking): Add reservation expiration tests** «Implementá tests unitarios para ExpireRetainedReservations que cubran reservas Retained no vencidas, Retained vencidas, Confirmed y Rejected. Agregá un test de integración con PostgreSQL que expire una reserva Retained y compruebe que otra reserva pueda persistirse con el mismo event_id y seat_id. Ejecutá dotnet build y los tests correspondientes a Booking.»

## Tras los merge de los PR #34 y #35

**Fix(Booking): Expire stale reservations before retrying seat allocation** «Implementá en Booking el reintento de creación de reservas cuando el asiento esté bloqueado por una reserva Retained vencida. Expirá la reserva mediante una actualización atómica utilizando TimeProvider y reintentá la inserción una sola vez. Si el conflicto persiste o la reserva no está vencida, mantené el rechazo SeatTaken. Conservá el manejo de duplicados por RequestId y evitá entidades fallidas en el tracking de EF Core. Actualizá los tests unitarios, de integración y stubs afectados. Ejecutá dotnet build y los tests de Booking.»

