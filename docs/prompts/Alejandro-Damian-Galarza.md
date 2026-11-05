# Historial de prompts — Alejandro Damián Galarza

**Componentes y tickets:** Booking, consumo de solicitudes de reserva (#18) y procesamiento de pago por gRPC (#19).
**Herramienta de IA usada:** agente de IA para planificar, implementar y documentar; Alejandro dirigía cada bloque y decidía cuándo avanzar.
**Revisión:** 2026-11-05. **Alcance:** prompts que cambiaron el diseño, el código, las pruebas o la validación. Es un registro individual del uso de IA, no una metodología aprobada por el equipo; las propuestas técnicas no se atribuyen automáticamente al equipo.

Este es el archivo de Alejandro para el [issue #9](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/9), que pide un archivo por integrante en `docs/prompts/Nombre-Apellido.md`.

## Cómo leer el registro

- **Prompt** es una cita textual breve del pedido de Alejandro. Cuando se omite parte de un pedido largo, se indica con `…`. Se conservan las palabras del mensaje, incluidos errores de escritura. Los textos de conexión y secretos se omiten.
- **Contexto** explica qué decisión o implementación había justo antes del pedido. Es una síntesis del historial, no parte del prompt.
- **Resultado** resume la respuesta o el cambio comprobable. Los estados históricos no se presentan como pendientes actuales.
- El agente proponía el siguiente bloque y registraba prompt, contexto y resultado tras la revisión de Alejandro. Una sugerencia del agente no figura como prompt usado hasta que Alejandro la envió.
- Se seleccionaron prompts útiles para reproducir el razonamiento. Se omitieron respuestas de cortesía, consultas repetidas sin cambio de dirección y comandos rutinarios de Git.

## Componente: Booking, consumo de solicitudes de reserva (#18)

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

### Estado de #18

El consumer y las reglas de Booking están implementados y el contrato compartido sustituyó al modelo privado ([PR #50](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/50), mergeado). La prueba con dos procesos evidencia el consumo competitivo de Booking ([guía](../booking/two-replica-validation.md)). El publicador real de Availability ([PR #52](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/52), ticket #15) ya está mergeado y se verificó un envío real a la suscripción `booking`. El [issue #18](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/18) sigue abierto por las decisiones del SDK y por la reproducción de la evidencia con dos réplicas por otra persona. Los detalles técnicos están en `docs/booking/`.

## Componente: Booking, procesamiento de pago por gRPC (#19)

### T19-F01-P01/P02

- **Prompt (fragmento literal):** «Revisá el issue #19, el contrato gRPC de #22 y las capas actuales de Booking y Payment. Confirmá flujo, estados, idempotencia, monto entero, moneda y límites del ticket. No modifiques código.»; «siguiendo el mismo criterio que ticket 18…»
- **Contexto:** Se iniciaba #19 mientras el trabajo de #18 seguía en otra rama.
- **Resultado:** Se separaron responsabilidades Booking/Payment y se creó una hoja de ruta F01–F09 con bloqueos y decisiones provisorias.

### T19-F01-P03

- **Prompt (fragmento literal):** «usemos el template para iniciat el ticket 19»
- **Contexto:** Alejandro había ensayado SDD de forma individual en #18 para seguir especificaciones, decisiones y prompts por bloque; el equipo no había adoptado esa práctica.
- **Resultado:** Se reutilizó la plantilla personal para iniciar spec y hoja de ruta de #19, sin presentar SDD como acuerdo del equipo.

### T19-F02-P01/P04

- **Prompt (fragmento literal):** «Validá el contrato definitivo de Payment para #19…»; «…mantengamos el definido en el 22 como amount_cents, si luego cambia se corregira…»
- **Contexto:** El `.proto` de #22 era candidato y existía una duda entre unidades enteras y centavos.
- **Resultado:** Se siguió `int64 amount_cents`, `currency` y `reservation_id`; se registró el cambio de criterio y se evitó copiar un `.proto` paralelo.

### T19-F02-P07/P08

- **Prompt (fragmento literal):** «alineemos los puntos concretos»; «Validemos la tabla de alineación con las clases 01–12 y el ejemplo gRPC…»
- **Contexto:** La revisión de issues había cambiado monto, expiración y dependencias de auth/auditoría.
- **Resultado:** Se corrigió la spec: N-Layer, cliente generado y HTTP/2 respaldados por clase 10/ejemplo; moneda, deadline y settlement quedaron como decisiones del proyecto. «Alineemos» significaba aplicar esa revisión concreta.

### T19-F02-P09/P10

- **Prompt (fragmento literal):** «Revisemos con #22 moneda, deadline, `Unavailable` ambiguo, límite de reentregas y conciliación…»; «Preparemos… las preguntas sobre moneda, reintentos con `Pending`/`Failed`, timeout y conciliación…»
- **Contexto:** Booking no podía fijar settlement sin saber cómo Payment representa cobros en curso y resultados perdidos.
- **Resultado:** Se consultó a #22; `Pending` pasó a `ABORTED`, `Failed` se definió sin cargo y `Unavailable` quedó ambiguo. Se separaron intentos de pago de reentregas del broker.

### T19-F02-P13

- **Prompt (fragmento literal):** «…proponé una matriz de settlement para Booking. Definí valores iniciales configurables… Incluí `ABORTED`, `Unavailable`, `DeadlineExceeded`, `UNSPECIFIED`, expiración y fallas de persistencia…»
- **Contexto:** Había respuestas parciales de #22 y una retención oficial de diez minutos.
- **Resultado:** Se propuso una matriz con tres intentos, espera de un segundo y deadline de cinco segundos, sin declararlos requisitos de la cursada ni cerrar la aceptación del cobro tardío.

### T19-F03-P01

- **Prompt (fragmento literal):** «entendido, avancemos con el **revisar este puerto y crear el fake para desarrollar y probar F04**»
- **Contexto:** El `.proto` no estaba aún en `develop`; podía probarse Business por un puerto propio.
- **Resultado:** Se definió `IPaymentChargeClient` y un fake capaz de devolver resultados y fallos controlados. «Avancemos» refería solo a este bloque.

### T19-F04-P01

- **Prompt (fragmento literal):** «…definir y probar con este fake las transiciones de la reserva ante pago aprobado, rechazado, temporalmente inaccesible y vencimiento durante el cobro…»
- **Contexto:** F03 ya tenía puerto y fake; Booking debía impedir confirmaciones tardías.
- **Resultado:** Business incorporó reintentos acotados y Data transiciones condicionales de `Retained` vigente.

### T19-F05-P01

- **Prompt (fragmento literal):** «…diseñar y probar con el fake la integración con el consumer y sus decisiones de settlement, sin activarlo…»
- **Contexto:** El adaptador real seguía bloqueado por #22.
- **Resultado:** El consumer continuó el pago tras una solicitud nueva o duplicado compatible; completó o envió a DLQ según resultado y propagó fallas de persistencia. Ambos interruptores siguieron apagados.

### T19-F05-P02/P05

- **Prompt (fragmento literal):** «Revisemos con el equipo el dead-letter provisorio de `Pending` agotado y aprobación tardía…»; «…definí el procedimiento de revisión de la DLQ de Booking…»
- **Contexto:** Había que distinguir cobro tardío conocido de resultado desconocido, sin exponer payloads.
- **Resultado:** Alejandro asumió la revisión de DLQ de Booking; [el procedimiento](../booking/payment-dlq-review.md) pide inspección inicial de solo lectura, cotejo con Payment por `reservation_id` y registro seguro de la decisión.

### T19-F06-P01

- **Prompt (fragmento literal):** «Revisá las 77 pruebas actuales de Booking y completá solo los casos de #19 que aún faltan: reentrega después de fallar la persistencia, competencia entre pago y vencimiento en PostgreSQL, y logs seguros…»
- **Contexto:** La lógica estaba probada con fake, pero faltaban estas carreras y fallos.
- **Resultado:** Se añadieron pruebas de reentrega, competencia en PostgreSQL y observabilidad sin body ni credenciales.

### T19-F08-P03

- **Prompt (fragmento literal):** «podemos avanzar parcialmente con el defensa oral?…»; «bien creemos el md al igual que la 18»
- **Contexto:** Había implementación local pero aún no gRPC real.
- **Resultado:** Se redactó una defensa parcial que separó evidencia con fake de integración real; después se actualizó el estado vigente al avanzar #22.

### T19-F03-P04

- **Prompt (fragmento literal):** «realicemos el desarrollo de forma recomendada, registrando los promps correspondientes a cada paso dado, indicame el avance y promp siguiente»
- **Contexto:** #22 ya había integrado `payment.proto` en `develop`.
- **Resultado:** Se implementó `GrpcPaymentChargeClient` con el cliente generado, dirección/deadline configurables, metadata y mapeo de errores; `Payment:Enabled` siguió apagado.

### T19-F07-P01/P02

- **Prompt (fragmento literal):** «Prepará y ejecutá una prueba local controlada de Booking contra Payment real y PostgreSQL, sin activar Azure Service Bus…»; «…Payment.Grpc como proceso separado y un driver local que invoque Booking…»
- **Contexto:** El adaptador estaba implementado, pero faltaba verificar ambos servicios y bases juntos.
- **Resultado:** Se probaron aprobación, rechazo, error, demora e idempotencia con Payment real/Testcontainers; luego Payment corrió como proceso separado HTTP/2. No se activó ASB.

### T19-F07-P03

- **Prompt (fragmento literal):** «…prepará el plan del smoke test integral con Azure Service Bus para una ventana coordinada, sin activar el consumer ni publicar mensajes antes de validar el plan»
- **Contexto:** #11, #15 y #24 aún condicionaban la prueba completa.
- **Resultado:** Se separó prueba técnica de Booking → Payment de recorrido integral desde Availability; el plan no habilitó consumer ni publicó mensajes.

### Ajustes del PR #53

- **Prompt (fragmento literal):** «cerremos los comentarios aplicables del PR: validar las opciones de pago al arrancar y limitar el tiempo total de reintentos; registrar el resultado desconocido incluso si la reserva venció…»
- **Contexto:** La revisión detectó riesgos de configuración, observabilidad y correlación.
- **Resultado:** Se ajustó validación al arranque, presupuesto de llamadas, logs y límite de `CorrelationId` a 64; se ampliaron pruebas.

### Precio y convención

- **Prompt (fragmento literal):** «definamos nosotros el precio que quede 150000 por defecto y configurable»; «si confirmemos eso…»
- **Contexto:** El valor `0` impedía habilitar Payment; Booking y Payment ya usaban `x-correlation-id`, pero #24 no lo había confirmado.
- **Resultado:** Booking fijó 150000 centavos ARS como predeterminado configurable y mantuvo `x-correlation-id` como convención propuesta. La confirmación grupal sigue pendiente.

### CI del PR

- **Prompt (fragmento literal):** «el pr tiene un accion con un error: … `SlowMode_DeadlineThenPending_DeadLettersAndChargeFinishesOnce`…»
- **Contexto:** CI observó `Complete` donde una prueba temporal esperaba siempre DLQ.
- **Resultado:** Se estabilizó la prueba para los resultados válidos según el orden de respuesta: `Confirmed`, DLQ/`Retained` o `PaymentUnavailable` si solo hubo deadlines. En todos los casos verifica un solo cargo. Suite local Release: 313/313.

### Estado de #19

El contrato de #22 y el cliente gRPC real están mergeados ([PR #40](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/40) y [PR #53](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/53)), con autenticación entre servicios (#51) y tests contra Payment real. `Payment:Enabled` sigue apagado por defecto. Las decisiones de settlement (cobro tardío sin reembolso, tres intentos, errores de credenciales a dead-letter) quedaron registradas en un comentario del [issue #19](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/19), que sigue abierto hasta la prueba manual Booking → Payment con Keycloak real. Los detalles técnicos están en [payment-client.md](../booking/payment-client.md).
