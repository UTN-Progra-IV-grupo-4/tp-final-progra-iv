# Historial de prompts — Martin Cumpe

**Componentes y tickets:** Autenticación de usuarios en Availability (#14) y autenticación entre servicios (#11).
**Herramienta de IA usada:** Devin (agente de IA en la terminal), con contexto persistente y capacidad de ejecutar comandos.
**Período:** 05/11/2026.
**Alcance:** prompts que implementaron autenticación OIDC para usuarios y Client Credentials para servicios. Es un registro individual del uso de IA, no una metodología aprobada por el equipo.

Este es el archivo de Martin para el [issue #9](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/9), que pide un archivo por integrante en `docs/prompts/Nombre-Apellido.md`.

## Cómo leer el registro

- **Prompt** es una cita textual breve del pedido de Martin. Cuando se omite parte de un pedido largo, se indica con `…`. Se conservan las palabras del mensaje, incluidos errores de escritura. Los textos de conexión y secretos se omiten.
- **Contexto** explica qué decisión o implementación había justo antes del pedido. Es una síntesis del historial, no parte del prompt.
- **Resultado** resume la respuesta o el cambio comprobable. Los estados históricos no se presentan como pendientes actuales.
- El agente proponía el siguiente bloque y registraba prompt, contexto y resultado tras la revisión de Martin. Una sugerencia del agente no figura como prompt usado hasta que Martin la envió.
- Se seleccionaron prompts útiles para reproducir el razonamiento. Se omitieron respuestas de cortesía, consultas repetidas sin cambio de dirección y comandos rutinarios de Git.

## Componente: Autenticación de usuarios en Availability (#14)

### T14-P01

- **Prompt (fragmento literal):** «Implementa este ticket»
- **Contexto:** El ticket #14 solicitaba implementar login/logout OIDC en el sitio MVC de Availability y proteger acciones de reserva con `[Authorize]`, con dos fases: desarrollo (handler con usuario fijo) y producción (Keycloak).
- **Resultado:** Se implementó `DevelopmentAuthenticationHandler` para modo desarrollo, `AccountController` con endpoints de login/logout, configuración OIDC en `Program.cs`, vistas de login y acceso denegado, y actualización del navbar con usuario autenticado. Se agregó paquete `Microsoft.AspNetCore.Authentication.OpenIdConnect`.

### T14-P02

- **Prompt (fragmento literal):** «¿Que solicita este ticket?»
- **Contexto:** Martin recibió una imagen de un ticket de GitHub y necesitaba entender el requerimiento.
- **Resultado:** El agente explicó que el ticket solicitaba login/logout OIDC en el sitio MVC, protección de acciones de reserva con `[Authorize]`, usuario anónimo redirigido a login, con dos fases: desarrollo (handler con usuario fijo) y producción (Keycloak).

### T14-P03

- **Prompt (fragmento literal):** «Necesito que me des los cambios realizados en formato .md para mi PR»
- **Contexto:** Martin necesitaba la descripción del PR en formato markdown según el template del proyecto.
- **Resultado:** Se generó un markdown con Issue vinculado, Resumen, tabla de Cambios, Cómo probarlo y Checklist, listo para copiar en la descripción del PR.

### T14-P04

- **Prompt (fragmento literal):** «Necesito que basado en el este listado de requisitos me digas si o que se desarrollo cumple con lo esperado»
- **Contexto:** Martin proporcionó el texto del ticket #14 y quería verificar si la implementación cumplía con los requisitos.
- **Resultado:** Se analizó cada requisito: configuración de clientes, middleware de validación, llamadas con token, validación de tokens, y se concluyó que la infraestructura base estaba al 70% implementada pero faltaba la integración real de tokens en las llamadas entre servicios.

### T14-P05

- **Prompt (fragmento literal):** «ok crea una rama y añade este propmt en el listado de propmts»
- **Contexto:** Martin quería documentar el historial de prompts en el archivo correspondiente y crear una rama para los cambios.
- **Resultado:** Se creó la rama `agregar-prompts-martin` y se inició este archivo de documentación.

## Componente: Autenticación entre servicios (#11)

### T11-P01

- **Prompt (fragmento literal):** «Necesito que basado en el este listado de requisitos me digas si o que se desarrollo cumple con lo esperado»
- **Contexto:** Martin proporcionó el texto del ticket #11 y quería verificar si la implementación anterior (en branch 11-Autenticación_entre_servicios) cumplía con los requisitos.
- **Resultado:** Se analizó que los paquetes, middleware y configuración estaban implementados, pero faltaba la integración real de tokens en las llamadas HTTP (Availability → Booking) y gRPC (Booking → Payment), y faltaba mover la autenticación a un proyecto compartido.

### T11-P02

- **Prompt (fragmento literal):** «Crea un mock para hacer la simulacion»
- **Contexto:** Como la integración real no estaba completa, se necesitaban mocks para simular la autenticación sin Keycloak corriendo.
- **Resultado:** Se crearon `MockKeycloakTokenService` (Availability), `MockPaymentTokenService` (Booking), y se actualizó `Program.cs` para soportar modo mock vs producción con la configuración `UseMockAuth`. Se agregó documentación completa en `docs/11-Autenticacion-entre-servicios.md`.

### T11-P03

- **Prompt (fragmento literal):** «No commites nada, muestrame los cambios al terminar»
- **Contexto:** Martin quería revisar los cambios antes de hacer commit.
- **Resultado:** Se mostró un resumen de cambios sin hacer commit, según lo solicitado.

## Estado de #14

La autenticación de usuarios en Availability.Web está implementada con dos modos: desarrollo (handler con usuario fijo) y producción (OpenID Connect con Keycloak). Los endpoints de reserva están protegidos con `[Authorize]` y el usuario anónimo es redirigido a login. El handler de desarrollo solo está activo en Development y falla si se intenta usar en otro entorno. [PR en branch `14-Availability_login_de_usuario_con_OIDC`](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/pull/new/14-Availability_login_de_usuario_con_OIDC).

## Estado de #11

La autenticación entre servicios tiene la infraestructura base configurada (paquetes, middleware, configuración) y soporta modo mock para desarrollo sin Keycloak. Los mocks generan tokens JWT localmente y el middleware valida según el modo. Falta mover la autenticación a un proyecto compartido e integrar los tokens en las llamadas reales HTTP y gRPC. La implementación está en branch `11-Autenticación_entre_servicios` pero la rama actual de trabajo (`14-Availability_login_de_usuario_con_OIDC`) ya tiene los avances de develop que incluyen autenticación entre servicios real en Booking y Payment.

## Referencias

- Issue #14 - Autenticación de usuarios en Availability
- Issue #11 - Autenticación entre servicios
- docs/14-autenticacion-usuarios-availability.md
- docs/11-Autenticacion-entre-servicios.md
- infra/keycloak/README.md
