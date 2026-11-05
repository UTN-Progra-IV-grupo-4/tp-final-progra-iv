# Historial de prompts — Lila Victoria Litvin

**Rol:** infraestructura local del proyecto (Docker, PostgreSQL, Keycloak), documentación del entorno y primer flujo funcional de solicitud de reserva con la publicación a Azure Service Bus.
**Herramienta de IA usada:** GitHub Copilot en VS Code, con memoria persistente y soporte de subagentes.
**Período:** 24/10/2026 al 05/11/2026.
**Alcance:** prompts que orientaron la infraestructura local, su documentación y el flujo de solicitud de reserva. Es un registro individual del uso de IA, no una metodología aprobada por el equipo.

## Cómo leer el registro

- Las citas son mensajes reales, agrupados por tema y en el orden en que los envié. Solo registro prompts, no respuestas del agente.
- Recorté repeticiones, muletillas, dudas sin valor y comandos que no sumaban. Cuando hice un recorte, marquélo con `…`.
- No cambié el sentido de lo pedido ni agregué pedidos que no hice.
- El texto de `Contexto` y `Resultado` es una síntesis posterior, no parte del prompt.
- Se omitieron secretos, connection strings y datos personales.

## 1. Infraestructura local y Docker (27/10 al 29/10)

- «Necesito dejar preparado el entorno local para levantar PostgreSQL y Keycloak con Docker sin dejar secretos ni valores hardcodeados en el repo.»
- «Necesito que la configuración de PostgreSQL sea segura y compatible con múltiples bases para cada servicio, pero sin romper la inicialización del contenedor.»
- «Revisá la guía para que cualquiera pueda levantar la infraestructura del proyecto, detectar errores comunes y entender cómo resolverlos en Windows y Docker.»
- «Armame un docker-compose con PostgreSQL, RabbitMQ y Keycloak y que use una red dedicada.»
- «Se puede hacer un solo contenedor con tres bases y tres usuarios, o conviene usar tres contenedores separados? Quiero que lo resolvamos en base a la decisión del ticket #6.»
- «No quiero dejar credenciales reales ni valores fijos en el repo. Quedate con un .env.example y variables vacías.»
- «Necesito que RabbitMQ quede como fallback y no levante con un docker compose up normal.»
- «Fijate la versión de Keycloak para no romper el stack con un pull nuevo.»
- «Vamos a cerrar el CONNECT entre bases para que cada servicio solo pueda conectarse a su base.»
- **Contexto:** se dejó listo el entorno local del proyecto con la infraestructura base, la red compartida, los volúmenes y la documentación para levantar y limpiar la stack. Se ajustó además la decisión de PostgreSQL para trabajar con una sola instancia y tres bases lógicas, con usuarios separados.

## 2. Documentación y validación del entorno (28/10 al 29/10)

- «Revisá la guía para que quede bien documentado cómo copiar .env.example a .env y levantar la infraestructura.»
- «Agregale la nota de que el script de init solo corre con el volumen vacío y que si cambian variables hay que hacer down -v.»
- «Si una password tiene comilla simple, rompe el SQL. Avisalo en README para que no lo usen.»
- «Necesito que la documentación quede clara y que no le falte el detalle del arranque local para el equipo.»
- **Contexto:** la infraestructura quedó documentada no solo como configuración, sino como un flujo operativo reproducible para todos.

## 3. Branches, flujo y continuidad del trabajo (30/10 al 05/11)

- «Necesito crear una nueva rama con la convención correcta: 15-request-seat-reservation desde develop.»
- «Mergeate develop primero en esta rama para que no haya conflictos con lo que ya actualizó el equipo.»
- «Dejame todo limpio para que la PR refleje solo los cambios de infraestructura y del ticket que corresponde.»
- **Contexto:** se mantuvo la convención del repositorio para ramas, merge desde develop y actualización del PR sin romper la base del proyecto.

## 4. Flujo de reserva y rebase sobre develop (05/11)

- «Crea en C# la clase ReservationRequestMessage en la carpeta/proyecto src/Contracts...»
- «Genera el contenido markdown con un diagrama Mermaid ... guardarlo en docs/contracts/reservation-request.md»
- «Crear una interfaz IMessagePublisher con el método Task PublishAsync...»
- «Crear la entidad de modelo de persistencia ReservationRequest ...»
- «Crear el caso de uso/handler para recibir la solicitud de reserva ...»
- «fijate de usar el develop actualizado» / «resolvé el rebase con develop»
- **Contexto:** la base del proyecto ya estaba levantada y la siguiente pieza funcional era el flujo de solicitud de reserva entre Availability y Booking. Se definió el contrato compartido, el handler, la persistencia y el endpoint autenticado, sin mezclar la infraestructura real del broker. Cuando apareció el rebase sobre develop, también quedó claro que no debía seguirse la estructura vieja si el equipo ya había mergeado avances más recientes del ticket #12.
- **Resultado:** se cerró el flujo de reserva con el contrato de mensajes, la lógica de validación y publicación, la persistencia de la solicitud y el endpoint protegido por autenticación. Luego se rehicieron los conflictos con develop y se dejó la rama consistente para la PR de la feature, sin incluir la implementación final del broker real.

## 5. Azure Service Bus en la rama 16 (05/11)

- «listo, ahora hagamos otra PR para lo de azure bus»
- «continuemos...»
- «ahora pasame el texto para la PR siguiendo el PULL_REQUEST_TEMPLATE.md de .github»
- **Contexto:** una vez cerrada la feature de reserva, quedó claro que la integración real del broker era un bloque separado. La rama 16 nació desde develop para aislar ese trabajo y mantener el alcance limpio: publicación real a Azure Service Bus, sin mezclarlo con la PR anterior.
- **Resultado:** se implementó el `AzureServiceBusMessagePublisher`, se integró el provider en `DependencyInjection` y en la configuración de appsettings, y se agregó una prueba de registro para validar que el resolver de DI devuelve el publisher correcto. También se dejó la rama lista para PR separada, con el texto del template y la validación del test puntual ejecutado.

## Estado al compartir

La infraestructura local quedó reproducible y documentada, y el flujo de solicitud de reserva (contrato, validación, persistencia y publicación a Azure Service Bus) quedó implementado en ramas separadas, alineadas con el flujo de ramas y PRs del equipo.
