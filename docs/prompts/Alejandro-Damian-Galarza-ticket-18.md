# Historial de prompts — Alejandro Damián Galarza — Ticket #18

**Componente:** Booking, consumo de solicitudes de reserva. **Revisión:** 2026-11-05. **Alcance:** prompts que cambiaron el diseño, el código, las pruebas o la validación. Este es un registro del trabajo de Alejandro con un agente de IA, no una metodología aprobada por el equipo.

Estos dos registros por ticket son una base para compartir. El [issue #9](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/9) pide **un archivo por integrante** antes de la entrega; la carpeta y el formato finales aún deben acordarse y estos registros podrán consolidarse en ese archivo.

## Cómo leer el registro

El agente ayudaba a ordenar el siguiente bloque y a registrar prompt, contexto y resultado después de la revisión de Alejandro. La decisión de avanzar o validar una propuesta era de Alejandro; por eso una sugerencia del agente no figura aquí como prompt usado hasta que él la envió.

- **Prompt** es una cita textual breve del pedido de Alejandro. Cuando se omite parte de un pedido largo, se indica con `…`. Se conservan las palabras del mensaje, incluidos errores de escritura. Los textos de conexión y secretos se omiten.
- **Contexto** explica qué decisión o implementación había justo antes del pedido. Es una síntesis del historial, no parte del prompt.
- **Resultado** resume la respuesta o el cambio comprobable. Los estados históricos no se presentan como pendientes actuales.
- Se seleccionaron prompts útiles para reproducir el razonamiento. Las respuestas de cortesía, consultas repetidas y comandos rutinarios de Git no se cuentan como instrucciones de implementación.

### F00, entorno

- **Prompt (fragmento literal):** «estoy en una nueva pc, no sabria decirte si el proyecto es posible usarlo , validemos si la pc esta en condiciones para resolver los tickets»
- **Contexto:** Antes de desarrollar había que comprobar herramientas, solución y base local.
- **Resultado:** Se verificaron .NET, Docker y PostgreSQL; las credenciales quedaron fuera de Git.

### T18-F01-P01/P02

- **Prompt (fragmento literal):** «realiemos primero el plan de desarrollo de la 18, entendimiento para que pueda defenderlo en la defensa oral»; «…hagamos una hoja de ruta con los promps y respuestas claves…»
- **Contexto:** Se necesitaba separar alcance de #18 y dependencias de otros tickets.
- **Resultado:** Se organizó el flujo Api → Business → Data, concurrencia, idempotencia, settlement y fases F01–F09.

### T18-F01-P04

- **Prompt (fragmento literal):** «repasemos cada ticket, y validemos si el ticket 18 esta en consiciones y alineado a como se va a desarrollar»
- **Contexto:** #15 aún no aportaba el contrato compartido; #17 definía persistencia y unicidad.
- **Resultado:** Se identificó que Data y Business podían avanzar y que el modelo del mensaje debía quedar aislado en Api.

### T18-F02-P01

- **Prompt (fragmento literal):** «provisoriamente, podria dejar como gap o bloque a corregir cuando este dispoinble y tener uno potencia? para el 15»
- **Contexto:** La clase de mensaje de #15 no estaba publicada.
- **Resultado:** Se registró un modelo privado reemplazable, sin agregar un contrato paralelo a `src/Contracts`.

### T18-F03-P01/P02

- **Prompt (fragmento literal):** «comencemos, para alinearnos al desarrollo actual. indicame paso a paso»; «cual es el procedimiento para completar f03»
- **Contexto:** Estaba definido el papel de PostgreSQL como árbitro entre réplicas.
- **Resultado:** Se implementó clasificación de `23505`, recuperación de EF Core y pruebas con PostgreSQL/Testcontainers de inserción, duplicado y competencia por asiento.

### T18-F04-P01/P02

- **Prompt (fragmento literal):** «dame detalle y especificacion de lo que debe hacer y como debe comportarse y el orden de como se debe desarrollaro»; «esto es scope del ticket 18 o afectamos a otros»
- **Contexto:** Data ya distinguía `RequestIdExists` y `SeatTaken`; faltaba la regla de negocio.
- **Resultado:** Se delimitó Business: `Retained`, `Rejected/SeatTaken`, duplicado compatible, conflicto de RequestId y `ExpiresAt` UTC.

### T18-F04-P03

- **Prompt (fragmento literal):** «continuemos»
- **Contexto:** Se refería a implementar **F04 Business** según la especificación y el alcance recién revisados.
- **Resultado:** Se agregó el procesador de solicitudes independiente del broker y se probó el comportamiento. El prompt breve no se interpreta como autorización para otro bloque.

### T18-F08-P01 — Primer uso personal de SDD

- **Prompt (fragmento literal):** «creemos una carpeta en doc llamada ssd desarrollo por especificaciones, podemos explayar el proyecto y lo que vayamos desarrollando…»; corrección posterior: «perdon, sdd spec drivern development».
- **Contexto:** Alejandro quería especificar cada bloque y mantener visibles sus decisiones, archivos y evidencias para contrastarlos con el código.
- **Resultado:** Se inició una spec y una hoja de ruta personales con trazabilidad de requisitos, implementación, pruebas y prompts. Fue una herramienta individual de trabajo, después respaldada fuera del repositorio.

### T18-F08-P02 — Alcance personal de SDD

- **Prompt (fragmento literal):** «CON EL EQUIPO  aun no se valida el uso de sdd, es para mi uso propio… en el readme… lo quitaria…»
- **Contexto:** La primera documentación podía dar a entender que SDD era una práctica aprobada por el equipo.
- **Resultado:** Se quitó esa presentación del README principal. El registro siguió como apoyo personal para desarrollar y defender #18, sin imponer una metodología al proyecto.

### T18-F05-P01/P02

- **Prompt (fragmento literal):** «…marcá expresamente como provisorias las decisiones técnicas…»; «Implementá F05 en Booking.Api usando las opciones guardadas en User Secrets y el modelo privado provisorio T18-G01…»
- **Contexto:** #15 seguía pendiente y F06 aún no había fijado el settlement.
- **Resultado:** Se configuró el consumer hospedado de Azure Service Bus, mapper y scope por mensaje; permaneció apagado por defecto. Las decisiones del SDK se registraron como provisorias.

### T18-F06-P01

- **Prompt (fragmento literal):** «Registrá la validación de la matriz de settlement de F06 y luego implementala. Completá Retained, Duplicate y SeatTaken; enviá a dead-letter los errores determinísticos…»
- **Contexto:** La matriz había sido revisada antes de conectarse a la subscription compartida.
- **Resultado:** Éxitos → `Complete`; errores determinísticos → `DeadLetter`; excepciones → propagación para reintento. Se probaron cada decisión y logs sin body ni secretos.

### T18-F02-P04

- **Prompt (fragmento literal):** «se definieron 10 minutos de retencion»; «Registrá la decisión del equipo de utilizar una retención de 10 minutos…»
- **Contexto:** La duración seguía abierta mientras F03–F06 usaban configuración.
- **Resultado:** Se fijó `00:10:00` y se probó `ExpiresAt = UtcNow + 10 minutos`.

### T18-F07-P01

- **Prompt (fragmento literal):** «realicemos las pruebas f07, cual es el mejor camino ordenado para darlo como satisfactorio»; luego «lo apruebo al plan»
- **Contexto:** F05/F06 estaban probados localmente; faltaba una ventana coordinada con el broker.
- **Resultado:** Tras la autorización del equipo se probaron alta, duplicado, conflicto, asiento ocupado, inválido y reentrega con PostgreSQL local. El consumer se detuvo al terminar.

### T18-F08-P08

- **Prompt (fragmento literal):** «avancemos con el f08»
- **Contexto:** La primera validación controlada de F07 ya tenía resultados y evidencias.
- **Resultado:** Se actualizaron la especificación, registro de implementación y defensa personal, separando pruebas automatizadas de smoke test real.

### T18-F02-P05 y F07-P02

- **Prompt (fragmento literal):** «usemos el que se encuentra en el pr del bnranch 15…»; «el f07 se debe repetir. realicemos esta prueba»
- **Contexto:** PR #35 mostraba un JSON candidato; el contrato todavía no estaba integrado. Se confirmó otra ventana de prueba.
- **Resultado:** Se alineó el mapper privado al candidato y se repitieron los escenarios F07. Esa evidencia fue una prueba con emisor controlado, no publicación real desde Availability.

### Contrato compartido y dos réplicas

- **Prompt (fragmento literal):** «realiza las tareas del 1 al 3»; «avancemos con los demas puntos»
- **Contexto:** El equipo informó que #35 ya estaba mergeado y pidió sustituir el modelo privado y obtener evidencia de competing consumers.
- **Resultado:** Se pasó a `Contracts.ReservationRequestMessage`; [la guía de dos réplicas](../booking/two-replica-validation.md) registra la prueba con la misma subscription y PostgreSQL. Estos prompts breves solo se entienden junto al plan inmediatamente anterior.

### Cierre documental

- **Prompt (fragmento literal):** «tambien acrtualiza la defensa oral…»
- **Contexto:** La integración del contrato compartido y la prueba de dos procesos cambiaron la evidencia defendible.
- **Resultado:** Se revisaron guía y defensa; quedó explícito que el publicador real de Availability pertenece a #15.

## Estado al compartir

El consumer y las reglas de Booking están implementados y el contrato compartido de #15 sustituyó el modelo privado. El [PR #50](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/50) se integró; el [issue #18](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/18) seguía abierto en la revisión. La prueba con dos procesos evidencia el consumo competitivo de Booking; el flujo completo desde la pantalla de Availability requiere el publicador real de #15. Las respuestas y pruebas detalladas permanecen en `docs/booking/` y en el historial personal respaldado fuera del repositorio.
