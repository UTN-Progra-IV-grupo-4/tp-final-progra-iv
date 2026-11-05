# Availability: estructura, dominio y persistencia (#12)

## Responsabilidades y dependencias

- Domain contiene entidades anémicas, sin referencias a EF Core.
- Application contendrá los casos de uso y depende de Domain.
- Infrastructure contiene el contexto, los mapeos y las migraciones de PostgreSQL.
- Web contiene MVC y el punto de composición: Program.cs referencia Infrastructure para registrar sus implementaciones. Los controladores deben utilizar Application.

Domain no depende de ninguna otra capa. La referencia de Web a Infrastructure se usa para conectar las implementaciones al iniciar la aplicación.

## Modelo y coordinación con #35

| Entidad | Propiedades | Significado |
| --- | --- | --- |
| Event | Id: Guid, Name: string (200), StartsAt: DateTimeOffset | Evento que ofrece asientos. |
| Seat | Id: Guid, EventId: Guid, Number: string (50) | Asiento de un evento. |
| ReservationRequest | Id: Guid, SeatId: Guid, UserId: string (200), RequestedAt: DateTimeOffset | Solicitud recibida, no una reserva confirmada. |

- Los futuros casos de uso generan los Guid antes de persistir. Los valores de eventos y asientos intercambiados con Booking deben ser los mismos.
- ReservationRequest no duplica EventId: quien construya el mensaje del #15 debe obtenerlo de Seat.EventId. La navegación debe cargarse o el caso de uso debe consultar explícitamente ese asiento.
- No hay IsAvailable ni estados de reserva en Availability: la ocupación y el resultado pertenecen a Booking. La ocupación se consulta con `GET /api/events/{eventId}/occupied-seats` (#21), que integra #13; el estado de una solicitud se consulta con el contrato de #21 desde #16.
- El #35 debe reutilizar esta entidad y ubicar su caso de uso en Application, evitando una segunda ReservationRequest dentro de Web. No se implementa el mapper ni el flujo de publicación en el #12.
- RequestedAt y StartsAt se persisten como timestamp with time zone. Application debe suministrar offset cero, por ejemplo DateTimeOffset.UtcNow, o normalizar un valor recibido con ToUniversalTime(). Npgsql rechaza un DateTimeOffset con otro offset.
- El mensaje compartido del #15 usa DateTime UTC: su mapper debe convertir RequestedAt mediante RequestedAt.UtcDateTime. No usar .DateTime, que pierde la información UTC. Mantener esta conversión fuera de Domain.
- UserId representa el claim sub de OIDC como texto; no es una FK a la base de Keycloak ni debe confiarse en una identidad recibida desde un formulario.
- El número de asiento es único por evento. Varias solicitudes pueden referirse al mismo asiento; Booking resuelve la concurrencia.
- Los borrados de eventos con asientos y asientos con solicitudes están restringidos para conservar las relaciones y el historial.
- Los campos y sus longitudes se coordinan con los consumidores. El catálogo de ejemplo de la demo fue confirmado en #12; #13 lo amplía a veinte asientos por evento.

## Convención SQL y migraciones

Tablas y columnas usan snake_case, como Booking: events, seats, reservation_requests, event_id, seat_id, requested_at, etc. Los mapeos son explícitos mediante ToTable y HasColumnName; no se agrega un paquete de convenciones.

InitialAvailability conserva su definición original. UseSnakeCaseNames renombra las tablas y columnas y ajusta sus restricciones, sin eliminar ni recrear las tablas. Esto permite actualizar una base donde ya se aplicó la primera migración conservando sus datos. En una base vacía se aplican ambas migraciones en orden. No editar el historial de migraciones aplicado ni borrar volúmenes para cambiar de convención.

Desde la raíz del repositorio:

```powershell
dotnet tool restore
dotnet build TpFinal.sln
dotnet ef migrations list --no-connect --project src/Availability/Availability.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/Availability/Availability.Infrastructure
dotnet ef migrations script --project src/Availability/Availability.Infrastructure --output "$env:TEMP/availability-schema.sql"
```

Se utiliza un único manifiesto: dotnet-tools.json en la raíz. La fábrica AvailabilityDbContextFactory permite generar migraciones y SQL sin iniciar MVC ni conectarse a PostgreSQL.

También se admite `--startup-project src/Availability/Availability.Web`. Ambos proyectos referencian Microsoft.EntityFrameworkCore.Design porque PrivateAssets=all impide que la referencia de Infrastructure se propague a Web. La fábrica crea el contexto en ambas variantes y para aplicar migraciones lee ConnectionStrings__Availability; no lee los user-secrets de Web.

## Configuración local con el compose (#3)

1. Copiar .env.example a .env y completar las variables del administrador y las tres bases: el init requiere todas. No versionar .env. El ejemplo debe conservar los valores vacíos.
2. Iniciar Docker Desktop y ejecutar `docker compose up -d postgres-db`.
3. Configurar la conexión de Availability con AVAILABILITY_DB_NAME, AVAILABILITY_DB_USER y AVAILABILITY_DB_PASSWORD. No usar el administrador ni los usuarios de los otros servicios.

El compose usa PostgreSQL 16, igual que la configuración de EF Core. El init solo corre con un volumen vacío: modificar .env no cambia usuarios ya creados. Revisar los datos antes de considerar eliminar un volumen. Evitar comillas simples en las contraseñas por la limitación documentada del script de init.

Web en Visual Studio usa **Administrar secretos de usuario** sobre Availability.Web:

```json
{
  "ConnectionStrings": {
    "Availability": "Host=<servidor>;Port=<puerto>;Database=<base>;Username=<usuario>;Password=<contraseña>"
  }
}
```

Para las herramientas de migración y como alternativa para Web, definir ConnectionStrings__Availability en la sesión local. En Git Bash, con un .env de confianza cuyos valores literales sean compatibles con Bash y con la cadena de conexión:

```bash
set -a
source .env
set +a
export ConnectionStrings__Availability="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=${AVAILABILITY_DB_NAME};Username=${AVAILABILITY_DB_USER};Password=${AVAILABILITY_DB_PASSWORD}"
dotnet ef database update --project src/Availability/Availability.Infrastructure
dotnet run --project src/Availability/Availability.Web --launch-profile http
```

Source ejecuta un archivo del shell: revisar su contenido antes de usarlo. Si la contraseña incluye caracteres especiales, usar una cadena correctamente escapada. No pegar credenciales en archivos versionados ni en comentarios del PR.

Compose lee .env para configurar sus contenedores; ASP.NET Core desde Visual Studio no lo lee automáticamente. No se ejecutan Migrate ni EnsureCreated al iniciar Web: la actualización del esquema es un paso explícito. La portada MVC no consulta tablas; abrirla no demuestra que funcione la persistencia.

## Datos de ejemplo

Persistence/Scripts/demo-data.sql contiene el catálogo confirmado de dos eventos y veinte asientos por evento (filas A y B, del 1 al 10), con IDs fijos para coordinar la demo con Booking. Conserva los seis IDs originales y agrega los restantes mediante generate_series, sin borrar asientos ni reservas existentes. Se ejecuta manualmente después de aplicar todas las migraciones y no forma parte del arranque de producción. Repetirlo omite los IDs existentes; no actualiza datos modificados.

Desde Git Bash:

```bash
docker compose exec -T postgres-db sh -c 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME" -v ON_ERROR_STOP=1' < src/Availability/Availability.Infrastructure/Persistence/Scripts/demo-data.sql
```

Para inspeccionar datos:

```bash
docker compose exec postgres-db sh -c 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME"'
```

Dentro de psql, `\dt` lista las tablas; `SELECT * FROM events;` y `SELECT * FROM seats;` muestran los ejemplos. `\q` permite salir. Si Git Bash informa que la terminal no es TTY, anteponer winpty al comando interactivo.

## Pruebas automáticas

Con Docker Desktop iniciado:

```bash
dotnet test tests/Availability/Availability.Tests/Availability.Tests.csproj
```

El proyecto está incluido en TpFinal.sln y también se descubre con `dotnet test TpFinal.sln`. Compilar no ejecuta los tests. Testcontainers inicia PostgreSQL 16 en contenedores descartables con puertos aleatorios, sin .env, compose ni secretos locales. Las credenciales constantes de la fixture son solo para esos contenedores de prueba.

Las clases de integración usan `[Collection(PostgreSqlCollection.Name)]` para compartir una fixture y ejecutar las pruebas sin concurrencia sobre la misma base. ResetAsync obtiene las tablas del modelo de EF Core y conserva el historial de migraciones. La prueba de actualización usa otro contenedor aislado porque necesita comenzar con el esquema anterior.

El workflow `.github/workflows/ci.yml` ejecuta la compilación y los tests de la solución en cada PR hacia `develop` o `main` y en cada push a esas ramas. Los tests de PostgreSQL usan Testcontainers con Docker en el runner. Los comandos locales permiten verificar los cambios antes de subirlos; el README describe cómo ejecutar los mismos pasos que el CI.

Se comprueban las migraciones sobre una base vacía, el renombrado conservando datos existentes, la ejecución repetida del seed y la escritura/lectura UTC mediante EF Core. Las restricciones se verifican ejecutando automáticamente constraints.sql, omitiendo únicamente los metacomandos de psql. Ese SQL prueba unicidad, claves foráneas, solicitudes múltiples y borrados restringidos, y revierte sus registros temporales.

El script sigue disponible para una comprobación manual después de migrar:

```bash
docker compose exec -T postgres-db sh -c 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME" -v ON_ERROR_STOP=1' < tests/Availability/Availability.Tests/constraints.sql
```

## Alcance

El #12 no implementa pantallas (#13/#16), login (#14), publicación y mapper (#15), consumo (#18), cobros ni la demo completa del PDF. Esos trabajos deben reutilizar este modelo y coordinar los contratos compartidos.

Referencias: [Npgsql para EF Core](https://www.npgsql.org/efcore/) y [fábrica de contexto para migraciones](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation).
