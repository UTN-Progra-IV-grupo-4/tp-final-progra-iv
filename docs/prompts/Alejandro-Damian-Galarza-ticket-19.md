# Historial de prompts — Alejandro Damián Galarza — Ticket #19

**Componente:** Booking, procesamiento de pago por gRPC. **Revisión:** 2026-11-05. **Alcance:** prompts que orientaron el contrato, las decisiones, la implementación y sus pruebas. Es un registro de uso de IA de Alejandro; las propuestas técnicas no se atribuyen automáticamente al equipo.

Estos dos registros por ticket son una base para compartir. El [issue #9](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/9) pide **un archivo por integrante** antes de la entrega; la carpeta y el formato finales aún deben acordarse y estos registros podrán consolidarse en ese archivo.

## Cómo leer el registro

El agente proponía el siguiente bloque y registraba prompt, contexto y resultado tras la revisión de Alejandro. Él decidía cuándo avanzar y qué hipótesis aceptar provisionalmente. Una sugerencia del agente no figura como prompt usado hasta que Alejandro la envió.

Los textos entre comillas son fragmentos de mensajes reales de Alejandro; `…` señala un recorte. **Contexto** y **resultado** son síntesis posteriores. Los prompts sugeridos por el agente que Alejandro no envió no se cuentan como prompts usados. Se omitieron secretos, respuestas de cortesía y consultas repetidas sin cambio de dirección.

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

## Estado al compartir

El contrato de #22 y el cliente gRPC real están implementados y probados localmente. El equipo aceptó no reembolsar automáticamente un cobro tardío; falta dejarlo asentado en el issue. El [PR #53](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/53) continúa en Draft y el [issue #19](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/19) abierto. Falta integrar `AddServiceBearerToken()` tras #51, probar autenticación, confirmar precio/header y políticas de settlement, y obtener CI verde. El recorrido completo desde Availability también depende del publicador real de #15. Los detalles técnicos actuales están en [payment-client.md](../booking/payment-client.md).
