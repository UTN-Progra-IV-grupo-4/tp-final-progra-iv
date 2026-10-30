# Availability: estructura, dominio y persistencia (#12)

## Responsabilidades y dependencias

- Domain: entidades con propiedades, sin lógica de casos de uso ni referencias a EF Core.
- Application: futura lógica de casos de uso; depende de Domain. No agregamos interfaces vacías sin un uso concreto.
- Infrastructure: contexto, mapeos de tablas y migraciones de PostgreSQL. Depende de las capas internas.
- Web: MVC y punto de composición. Program.cs referencia Infrastructure para registrar el contexto mediante inyección de dependencias. Los futuros controladores deben usar Application.

El punto de composición conecta las implementaciones concretas al iniciar la aplicación. Esto explica la referencia adicional Web -> Infrastructure; no habilita acceso directo a EF Core desde los controladores. Domain sigue sin depender de ningún otro proyecto.

## Modelo propuesto, pendiente de coordinación con Booking

| Entidad | Propiedades | Significado |
| --- | --- | --- |
| Event | Id: Guid, Name: string (200), StartsAt: DateTimeOffset | Evento que ofrece asientos. |
| Seat | Id: Guid, EventId: Guid, Number: string (50) | Asiento de un evento; Number puede ser A1, A2, etc. |
| ReservationRequest | Id: Guid, SeatId: Guid, UserId: string (200), RequestedAt: DateTimeOffset | Solicitud recibida; no representa el resultado de la reserva. |

Las propiedades de navegación expresan las relaciones entre entidades; EF Core usa las claves foráneas para almacenarlas. ReservationRequest no repite EventId porque se obtiene del asiento.

- Los futuros casos de uso generan los Guid antes de guardar. No son números autoincrementales de PostgreSQL. Coordinar tipos e IDs compartidos con #17, #15 y #18 antes de integrar servicios.
- UserId almacenará el claim sub del usuario autenticado. No lo recibiremos como una identidad confiable desde el formulario. Confirmar longitud y uso con #14; no hay FK a la base de Keycloak.
- StartsAt y RequestedAt deben escribirse en UTC (por ejemplo, DateTimeOffset.UtcNow). PostgreSQL usa timestamp with time zone; la zona para mostrar al usuario se resuelve en la presentación.
- El número de asiento es único por evento. No hay índice único sobre SeatId en las solicitudes: distintos usuarios pueden solicitar el mismo asiento. Booking decide quién obtiene la reserva.
- Se restringen los borrados de eventos con asientos y asientos con solicitudes para evitar pérdida accidental de historial.
- No hay IsAvailable ni estados de reserva en Availability: la ocupación y el resultado pertenecen a Booking. La consulta se resolverá en #13 y #16.
- Las longitudes son una propuesta técnica inicial, no un requisito explícito del PDF. La validación de entradas corresponde a los futuros casos de uso.
- Convención de nombres pendiente de confirmar con el equipo: por ahora las tablas y columnas usan PascalCase (Events, Seats, ReservationRequests). Es válido en PostgreSQL; las consultas SQL manuales requieren comillas dobles, como muestra demo-data.sql. EF Core genera esas comillas automáticamente. Si el equipo elige snake_case, actualizar conjuntamente los mapeos, la migración y el script de ejemplo antes de aplicarlos en una base compartida.

## Migraciones sin PostgreSQL

Desde la raíz del repositorio:

```powershell
dotnet tool restore
dotnet build TpFinal.sln
dotnet ef migrations list --no-connect --project src/Availability/Availability.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/Availability/Availability.Infrastructure
dotnet ef migrations script --project src/Availability/Availability.Infrastructure --output "$env:TEMP/availability-schema.sql"
```

La fábrica AvailabilityDbContextFactory configura el proveedor sin conexión. Así se pueden generar migraciones y SQL sin iniciar MVC ni disponer del contenedor. No inventa credenciales ni crea una base. Si agregamos o cambiamos propiedades, se genera otra migración con `dotnet ef migrations add NombreDelCambio --project src/Availability/Availability.Infrastructure`.

También se puede elegir Web como proyecto de inicio:

```powershell
dotnet ef migrations has-pending-model-changes --project src/Availability/Availability.Infrastructure --startup-project src/Availability/Availability.Web
```

`--project` indica dónde están el contexto y las migraciones. `--startup-project` selecciona el proyecto que usan las herramientas como inicio; al omitirlo, en estos comandos se usa Infrastructure. Ambos proyectos incluyen Microsoft.EntityFrameworkCore.Design porque ese paquete, marcado PrivateAssets=all, no se propaga de Infrastructure a Web. La fábrica sigue siendo la responsable de crear el contexto en ambas variantes: incluso con Web como inicio, para aplicar migraciones se usa la variable ConnectionStrings__Availability, no los user-secrets de Web.

## Integración con el compose del issue #3

El #3 ya está integrado. docker-compose.yml usa PostgreSQL 16 y su script de init crea las tres bases y sus usuarios. Availability usa exclusivamente AVAILABILITY_DB_NAME, AVAILABILITY_DB_USER y AVAILABILITY_DB_PASSWORD. No usa el administrador ni los usuarios de Booking o Payment para ejecutar la aplicación o migrar. Tanto la fábrica como el registro de EF Core indican PostgreSQL 16.

Los nombres, contraseñas y puertos siguen siendo configuración local, no propiedades pendientes en las entidades. En este entorno se eligieron availability_db, availability_user y el puerto 5432 para desarrollo. Cada integrante debe usar sus propios valores de .env.

Para un entorno nuevo, copiar .env.example a .env y completar las variables de las tres bases y del administrador: el init exige las tres aunque solo probemos Availability. Mantener .env fuera de Git. El ejemplo versionado debe conservar valores vacíos. No usar comillas simples en las contraseñas mientras el script de init tenga esa limitación.

Con Docker Desktop iniciado, levantar solo lo necesario para #12:

```bash
docker compose up -d postgres-db
docker compose ps postgres-db
```

No se necesitan Keycloak, RabbitMQ ni la conexión de Azure para estas pruebas. El init solo se ejecuta al crear un volumen vacío: cambiar .env no cambia los usuarios de una base ya inicializada. No borrar el volumen para arreglar una conexión sin revisar antes qué datos contiene.

Para MVC en desarrollo, guardar la conexión real con **Administrar secretos de usuario** sobre Availability.Web en Visual Studio. Estructura de ejemplo (los textos entre < > se reemplazan localmente; no son valores acordados):

```json
{
  "ConnectionStrings": {
    "Availability": "Host=<servidor>;Port=<puerto>;Database=<base>;Username=<usuario>;Password=<contraseña>"
  }
}
```

Alternativa para MVC y para las herramientas de migración: configurar la variable de entorno `ConnectionStrings__Availability` solo en la sesión local. La fábrica de diseño lee esa variable; no lee los user-secrets de Web. No pegar secretos en scripts versionados, capturas o historial compartido.

En Git Bash, para el .env local de desarrollo con valores literales de confianza (los generados en esta máquina usan contraseñas hexadecimales), se puede configurar esa variable en la misma terminal:

```bash
set -a
source .env
set +a
export ConnectionStrings__Availability="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=${AVAILABILITY_DB_NAME};Username=${AVAILABILITY_DB_USER};Password=${AVAILABILITY_DB_PASSWORD}"
```

No ejecutar source sobre archivos ajenos sin revisar: es una instrucción del shell. Este procedimiento presupone valores compatibles con Bash y con el formato de conexión; para contraseñas con caracteres especiales, usar user-secrets y una cadena correctamente escapada.

Con esa variable y PostgreSQL disponible:

```powershell
dotnet ef database update --project src/Availability/Availability.Infrastructure
```

Para iniciar Web con la conexión de esa sesión de Git Bash:

```bash
dotnet run --project src/Availability/Availability.Web --launch-profile http
```

Desde Visual Studio se utilizan los user-secrets de Availability.Web. La portada MVC no consulta aún tablas; abrirla no demuestra que funcione la persistencia. Las pantallas de eventos y asientos corresponden al #13.

No se ejecuta Migrate ni EnsureCreated al arrancar Web. Esto evita que cada réplica intente modificar el esquema y mantiene explícito el paso de migración. La portada MVC puede abrirse sin conexión; al pedir el contexto sin configuración se recibe un error explicativo.

Compose lee el .env local para configurar PostgreSQL, pero ASP.NET Core ejecutado desde Visual Studio no lo lee automáticamente. Web usa user-secrets o variables de entorno. El .env real ya existe en este entorno y Git lo ignora; no se guarda ninguna contraseña en el repositorio.

## Datos de ejemplo y pruebas de base de datos

Persistence/Scripts/demo-data.sql propone dos eventos y seis asientos con IDs fijos. Es opcional, manual y solo para desarrollo. Ejecutarlo con un cliente PostgreSQL sobre la base de Availability, después de aplicar las migraciones. Repetirlo omite IDs ya existentes; no actualiza datos modificados previamente. No está conectado al inicio de la aplicación.

Después de migrar, ejecutar los ejemplos y las pruebas desde Git Bash:

```bash
docker exec -i postgres-db sh -c 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME" -v ON_ERROR_STOP=1' < src/Availability/Availability.Infrastructure/Persistence/Scripts/demo-data.sql
docker exec -i postgres-db sh -c 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME" -v ON_ERROR_STOP=1' < tests/Availability.Database/constraints.sql
```

Las variables del comando entre comillas simples se resuelven dentro del contenedor; no se pega la contraseña en el comando. La conexión TCP a 127.0.0.1 comprueba la autenticación del usuario de Availability. Las pruebas usan datos con IDs nuevos en una transacción y terminan con ROLLBACK: no dejan solicitudes ficticias ni borran registros existentes.

Para inspeccionar las tablas desde Git Bash:

```bash
docker exec -it postgres-db sh -c 'PGPASSWORD="$AVAILABILITY_DB_PASSWORD" exec psql -h 127.0.0.1 -U "$AVAILABILITY_DB_USER" -d "$AVAILABILITY_DB_NAME"'
```

Dentro de psql, `\dt` lista las tablas. `SELECT * FROM "Events";` muestra los eventos y `SELECT * FROM "Seats";` los asientos. Para salir, usar `\q`. Si Git Bash muestra que la terminal no es TTY, anteponer `winpty` al comando interactivo.

Verificar:

1. La migración crea Events, Seats y ReservationRequests, y queda registrada en __EFMigrationsHistory.
2. El script de ejemplo carga dos eventos y seis asientos en una base vacía.
3. Un asiento con el mismo número y evento es rechazado; el mismo número en otro evento es válido.
4. No se admiten asientos con un evento inexistente ni solicitudes con un asiento inexistente.
5. Dos solicitudes distintas para el mismo asiento se guardan; no se impone una única solicitud por asiento.
6. No se pueden borrar asientos con solicitudes. Las fechas UTC se guardan y se recuperan correctamente.

## Fuera del #12

No se implementan login (#14), publicación (#15), consumo/concurrencia/idempotencia (#18), cobros, endpoints de consulta ni la demo completa del PDF. El contrato de mensaje en Contracts sigue pendiente de acuerdo entre Availability y Booking. Esta base no cierra esos issues.

El PDF también pide historial de prompts por integrante. Conservar y entregar el historial real con nombre y apellido según #9; este documento no sustituye ese historial.

## Verificación realizada en este avance

- La solución completa compila sin errores ni advertencias.
- EF Core encuentra InitialAvailability y confirma que no hay cambios del modelo pendientes de migrar.
- Se generó y revisó el SQL de las tres tablas, las claves foráneas y el índice único por evento/número, sin conectar a PostgreSQL.
- La portada MVC respondió HTTP 200 sin una conexión de base configurada.
- El 30/10/2026 se inició postgres-db desde el compose integrado y se aplicó InitialAvailability usando availability_user sobre availability_db. No fue necesario modificar ni regenerar la migración.
- Se cargaron los dos eventos y seis asientos. Al terminar las pruebas quedaron cero solicitudes: los registros temporales se revirtieron.
- Pasaron las pruebas de tests/Availability.Database/constraints.sql: números únicos por evento, claves foráneas, IDs de solicitud únicos, solicitudes múltiples para un mismo asiento, borrados restringidos e instante persistido en PostgreSQL.
- Se comprobó que availability_user no tiene CONNECT a booking_db ni payment_db, y booking_user no tiene CONNECT a availability_db.
- Se guardó ConnectionStrings:Availability en los user-secrets locales de Web con las credenciales de este .env. No se versionaron secretos. Para otro equipo hay que repetir su configuración local.
- Estas pruebas comprueban el esquema y las restricciones reales de PostgreSQL; los futuros casos de uso y pantallas de #13/#15 todavía no están implementados ni probados.

Referencias técnicas: [proveedor Npgsql para EF Core](https://www.npgsql.org/efcore/) y [fábrica de contexto para las herramientas de EF Core](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation).
