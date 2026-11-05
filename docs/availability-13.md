# Availability: eventos y asientos públicos (#13)

## Flujo y capas

La portada redirige a `/eventos`. Cualquier visitante puede listar eventos y abrir `/eventos/{eventId}/asientos` sin iniciar sesión. El mapa usa un grupo de radios: permite elegir un solo asiento. Los ocupados están grises y deshabilitados; además, el controlador rechaza elecciones de asientos ocupados, ajenos al evento o con disponibilidad desconocida.

La elección se refleja mediante un GET y no registra una solicitud, retiene un asiento ni publica mensajes. El envío pertenece al #15. Al confirmar la elección se vuelve a consultar la disponibilidad. El enlace de actualización permite consultar otra vez el mapa; todavía no hay actualización automática ni notificaciones en tiempo real.

EventsController utiliza EventQueryService de Application. Ese servicio combina IEventCatalog (catálogo de Availability) con ISeatOccupancyQuery (ocupación). Infrastructure implementa las consultas. Las vistas no acceden a EF Core ni deducen ocupación desde ReservationRequest.

## Ejecutar la demostración sin base ni Booking

```bash
dotnet run --project src/Availability/Availability.Web --launch-profile http
```

Abrir `http://localhost:5212/eventos`. El perfil http activa Development, donde EventQueries:Source está configurado como Demo. Se muestran dos eventos con veinte asientos cada uno, filas A y B del 1 al 10. A2 y B3 están ocupados de forma simulada en cada evento. El aviso de demostración identifica esos datos y su ocupación.

En Visual Studio, iniciar Availability.Web con el perfil http. No hace falta .env, base activa, Keycloak ni Service Bus para esta demostración. Los IDs en memoria coinciden con los del seed. El modo Demo se rechaza fuera de Development para evitar presentarlo como información real.

## Catálogo PostgreSQL y cliente HTTP de Booking

La implementación EfEventCatalog consulta eventos y asientos reales con AsNoTracking. Para usarla, preparar PostgreSQL y ConnectionStrings:Availability según availability-12.md, aplicar las migraciones y ejecutar el seed actualizado. En la misma terminal Git Bash:

```bash
export EventQueries__Source=Database
export Booking__BaseUrl=http://localhost:5088/
dotnet run --project src/Availability/Availability.Web --launch-profile http
```

Para volver a la demo en esa terminal, ejecutar `unset EventQueries__Source`. appsettings.json usa Database como valor predeterminado y appsettings.Development.json selecciona Demo para desarrollo. Se aceptan únicamente los valores Demo y Database.

En modo Database, BookingSeatOccupancyQuery consume `GET /api/events/{eventId}/occupied-seats` según el contrato del #21, ya implementado en Booking. Se registra como cliente tipado con IHttpClientFactory y no referencia los proyectos de Booking: su DTO de transporte es privado de Infrastructure. Demo sigue utilizando la ocupación simulada.

**La obtención del token real sigue pendiente del #11.** IBookingServiceTokenProvider separa esa responsabilidad del cliente HTTP. Su registro actual, PendingBookingServiceTokenProvider, devuelve null: sin token no se envía una llamada anónima a Booking y el mapa mantiene la disponibilidad sin confirmar. Tampoco se permite elegir asientos en ese estado. No se hace fallback a la ocupación simulada.

El #11 deberá reemplazar el proveedor por client credentials de availability-service, con la audiencia y los permisos que acepta Booking (ver `docs/booking/authentication.md`). El visitante sigue siendo anónimo; no se usa el token de availability-web ni se reenvía uno del usuario. El proveedor debe respetar el CancellationToken. No se guardan tokens estáticos ni client secrets en appsettings.

Configuración de la consulta:

| Clave | Variable de entorno | Uso |
| --- | --- | --- |
| Booking:BaseUrl | Booking__BaseUrl | URL HTTP/HTTPS de Booking, vacía por defecto; sin ella no se confirma disponibilidad. El perfil http local de Booking usa http://localhost:5088/. |
| Booking:TimeoutSeconds | Booking__TimeoutSeconds | Límite total de la consulta, incluido el proveedor de token. Por defecto 2 segundos; se aceptan de 1 a 30. |

Se validan el esquema HTTP/HTTPS y la ausencia de credenciales, query y fragmento en BaseUrl. Una configuración inválida falla al registrar el modo Database. Para volver a dejar la URL sin configurar, ejecutar `unset Booking__BaseUrl` en esa terminal.

Solo una respuesta 200 con JSON válido, el eventId consultado y una lista de UUIDs distintos y no vacíos confirma la ocupación. seatIds vacío confirma que no hay ocupados. Campos faltantes, JSON inválido, otro evento, error de red, timeout, 401, 403, 404 o 5xx mantienen la disponibilidad sin confirmar. La cancelación del visitante se propaga; no se convierte en un timeout. No se registran tokens ni cuerpos de respuesta en los logs. El catálogo de Availability decide si existe un evento; Booking puede devolver una lista vacía para un evento sin reservas.

Booking decide qué reservas ocupan asientos, incluida la expiración de las retenciones. Availability no implementa por su cuenta el tiempo de retención de diez minutos ni libera reservas. Hay consistencia eventual entre el envío de una solicitud y su procesamiento: mientras Booking no la procese, un asiento puede seguir figurando libre. Seleccionarlo tampoco garantiza que otra solicitud no lo obtenga primero.

## Seed ampliado y compatibilidad

demo-data.sql contiene los dos eventos confirmados y genera cuarenta asientos con generate_series. No requiere una migración nueva porque el esquema no cambia. Se puede ejecutar sobre el catálogo anterior: no borra registros y conserva los seis IDs originales, por lo que las referencias existentes siguen siendo válidas.

La última parte del UUID identifica un número predecible:

| Evento | Asientos | Sufijos |
| --- | --- | --- |
| Conferencia | A1, A2, A3 | 1, 2, 3 |
| Recital | A1, A2, A3 | 4, 5, 6 |
| Conferencia | A4..A10, B1..B10 | 7..23 |
| Recital | A4..A10, B1..B10 | 24..40 |

DemoCatalogData utiliza la misma asignación. Una prueba compara todos sus IDs y números con el seed ejecutado en PostgreSQL. ON CONFLICT(id) DO NOTHING permite repetir el seed sin duplicar filas; no corrige modificaciones manuales que alguien haya hecho sobre IDs existentes.

## Verificación

Con Docker Desktop iniciado:

```bash
dotnet build TpFinal.sln
dotnet test tests/Availability/Availability.Tests/Availability.Tests.csproj
```

Los tests cubren el esquema del #12, el seed repetible con cuarenta asientos, la conservación de referencias al ampliar el catálogo, la igualdad entre memoria y PostgreSQL, las consultas de disponibilidad y la validación del controlador. Los tests de PostgreSQL usan contenedores descartables y no modifican el compose local.

BookingSeatOccupancyQueryTests verifica el método y ruta HTTP, el encabezado Bearer de servicio, respuesta vacía válida, errores HTTP, JSON inválido, timeout, cancelación y ausencia de token/configuración. BookingOccupancyEndpointTests ejecuta el pipeline MVC y el adaptador HTTP con respuestas controladas: un visitante anónimo ve los ocupados en gris y, si Booking responde 401/403/503, todos los asientos quedan sin confirmar y deshabilitados. Los tokens de esos tests son ficticios y no se envían a servicios reales. Estas pruebas no sustituyen la verificación autenticada con Keycloak y Booking del #11.

Para comprobar visualmente: abrir eventos, entrar a un mapa, verificar sus dos filas, elegir A1 y pulsar Elegir asiento. Debe aparecer la elección sin crear una reserva. A2 y B3 deben verse grises. Una URL que intente seleccionar uno de esos asientos debe mostrar un error. Un evento inexistente devuelve 404.

## Pendientes para cerrar #13

- [x] Vistas y controladores públicos con implementación en memoria.
- [x] Selección de un solo asiento y ocupados grises/deshabilitados en la demo.
- [x] Seed confirmado ampliado a veinte asientos por evento, con IDs anteriores conservados.
- [x] Consulta de catálogo PostgreSQL y pruebas de integración.
- [x] Cliente HTTP del contrato de ocupación del #21 y pruebas con respuestas controladas.
- [ ] Token de servicio availability-service y permisos definidos por #11.
- [ ] Verificación real de punta a punta con Keycloak y Booking: reserva retenida/confirmada en gris, vencida/rechazada libre y fallo de Booking sin disponibilidad falsa.

El ticket permanece abierto hasta completar esa integración. El mock es una herramienta de desarrollo, no evidencia de que el flujo real esté terminado.

Referencia técnica: [clientes tipados con IHttpClientFactory](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory).
