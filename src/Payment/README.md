# Payment: levantar y probar en local

Guía para levantar el servicio Payment ([#22](https://github.com/UTN-Progra-IV-grupo-4/tp-final-progra-iv/issues/22)) en una máquina de desarrollo y probar el contrato `payment.PaymentService/Charge` con grpcurl, sin depender de Booking.

Todos los comandos se ejecutan desde la **raíz del repositorio**, no desde esta carpeta.

## 1. Requisitos

| Herramienta | Para qué | Instalación |
|---|---|---|
| .NET SDK 10 | Compilar y ejecutar | La versión está fijada en `global.json` |
| Docker Desktop | PostgreSQL | Tiene que estar abierto antes de empezar |
| dotnet-ef 10.0.14 | Migraciones | `dotnet tool install --global dotnet-ef --version 10.0.14` |
| grpcurl | Llamar al servicio como si fuera Booking | `winget install --id fullstorydev.grpcurl -e` |

Después de instalar con `winget` o `dotnet tool`, abrí una terminal nueva para que se actualice el `PATH`.

## 2. Levantar PostgreSQL

1. Copiá `.env.example` a `.env` en la raíz del repositorio y completá las variables de PostgreSQL. El script de inicialización exige **las nueve** variables `AVAILABILITY_DB_*`, `BOOKING_DB_*` y `PAYMENT_DB_*`. Si falta alguna, el contenedor no termina de iniciar. El `.env` no se sube al repositorio.

   ```dotenv
   POSTGRES_PORT=5432
   PAYMENT_DB_NAME=payment_db
   PAYMENT_DB_USER=payment_user
   PAYMENT_DB_PASSWORD=<una contraseña local>
   ```

2. Desde la raíz del repositorio, levantá solo PostgreSQL:

   ```bash
   docker compose up -d postgres-db
   ```

3. Verificá que se crearon las bases:

   ```bash
   docker logs postgres-db | grep "initialization completed"
   ```

> **Si ya tenés PostgreSQL instalado en Windows** (servicio `postgresql-x64-NN`), está ocupando el puerto 5432 y `localhost:5432` va a llegar a ese servidor, no al de Docker. Usá `POSTGRES_PORT=5433` en el `.env` y `Port=5433` en la cadena de conexión. Una pista: si el error de Npgsql aparece en español, te respondió el PostgreSQL de Windows; el de Docker responde en inglés.

> El script de inicialización corre **solo cuando el volumen está vacío**. Si cambiás nombres o contraseñas en el `.env` después del primer arranque, recreá el volumen con `docker compose down -v` y después `docker compose up -d postgres-db`. Esto borra todos los datos.

## 3. Cadena de conexión

Payment lee `ConnectionStrings:Payment` desde user-secrets. Usá los mismos valores del `.env`:

```bash
dotnet user-secrets set "ConnectionStrings:Payment" "Host=localhost;Port=5432;Database=payment_db;Username=payment_user;Password=<la del .env>" --project src/Payment/Payment.Grpc
```

Sin esta cadena, Payment no arranca y muestra un mensaje que indica dónde configurarla.

## 4. Aplicar la migración

```bash
dotnet ef database update --project src/Payment/Payment.Data --startup-project src/Payment/Payment.Grpc
```

En la primera ejecución vas a ver un `Failed executing DbCommand` sobre `__EFMigrationsHistory`. Es normal: EF busca la tabla de historial, ve que no existe, la crea y sigue.

Para verificar que quedó el índice único, que garantiza un cobro por reserva:

```bash
docker exec postgres-db psql -U postgres -d payment_db -c "\d charges"
```

Tiene que aparecer `"IX_charges_reservation_id" UNIQUE, btree (reservation_id)`. Si en el `.env` cambiaste `POSTGRES_ADMIN_USER`, usá ese usuario en lugar de `postgres`.

## 5. Arrancar Payment

**Visual Studio:** elegí el perfil `http` y ejecutá `Payment.Grpc`. Escucha en `http://localhost:5183`, solo HTTP/2.

**Línea de comandos:**

```bash
dotnet run --project src/Payment/Payment.Grpc --launch-profile http
```

### Elegir el modo de la pasarela simulada

El resultado lo decide `SimulatedGateway:Mode`, no el monto. Para cambiarlo, configurá la variable de entorno y **reiniciá solo Payment**:

| Modo | Qué hace | Qué recibe Booking |
|---|---|---|
| `Approve` (por defecto) | Aprueba al instante | `CHARGE_STATUS_APPROVED` |
| `Decline` | Rechaza con "Fondos insuficientes" | `CHARGE_STATUS_DECLINED` y `failureReason` |
| `Slow` | Espera `SlowDelaySeconds` (15 s) y aprueba. Ignora la cancelación del cliente | `DEADLINE_EXCEEDED` si el deadline es menor; los reintentos reciben `ABORTED` hasta que termina |
| `Error` | Falla técnica; el cobro queda `Failed` | `UNAVAILABLE` |

```bash
# Git Bash
SimulatedGateway__Mode=Decline dotnet run --project src/Payment/Payment.Grpc --launch-profile http
```

```powershell
# PowerShell
$env:SimulatedGateway__Mode = "Decline"; dotnet run --project src/Payment/Payment.Grpc --launch-profile http
```

En Visual Studio: *Propiedades del proyecto → Depurar → Variables de entorno*, o agregala en el perfil `http` de `launchSettings.json` **sin commitear el cambio**.

Un valor de modo que no existe frena el arranque, por `ValidateOnStart`.

## 6. Llamar al servicio con grpcurl

Payment no expone reflexión, así que grpcurl lee el contrato desde `src/Contracts/Protos`. Ejecutá los comandos desde la raíz del repositorio.

**Git Bash:**

```bash
grpcurl -plaintext -import-path src/Contracts/Protos -proto payment.proto \
  -H 'x-correlation-id: demo-1' \
  -d '{"reservation_id":"11111111-1111-4111-8111-000000000001","amount_cents":150000,"currency":"ARS"}' \
  localhost:5183 payment.PaymentService/Charge
```

**PowerShell**, donde las comillas del JSON se pierden al pasarlo como argumento. Por eso se manda por la entrada estándar con `-d '@'`:

```powershell
'{"reservation_id":"11111111-1111-4111-8111-000000000001","amount_cents":150000,"currency":"ARS"}' |
  grpcurl -plaintext -import-path src/Contracts/Protos -proto payment.proto `
  -H "x-correlation-id: demo-1" -d '@' localhost:5183 payment.PaymentService/Charge
```

Respuesta esperada:

```json
{
  "status": "CHARGE_STATUS_APPROVED",
  "chargeId": "22991638-2296-42fc-956f-8f4e0df3037a"
}
```

En la consola de Payment, cada log del cobro muestra el scope `=> CorrelationId:demo-1`.

> La primera llamada después de arrancar tarda uno o dos segundos más, porque EF arma el modelo. En una demo, hacé antes una llamada de calentamiento con un `reservation_id` descartable.

## 7. Casos de prueba

Usá un `reservation_id` nuevo por caso, salvo cuando el caso es justamente reintentar. Un GUID es válido con cualquier contenido; por ejemplo, `22222222-2222-4222-8222-000000000001`.

| # | Modo | Pedido | Resultado esperado |
|---|---|---|---|
| 1 | `Approve` | Id nuevo | `APPROVED` con un `chargeId` |
| 2 | `Approve` | El mismo id del caso 1 | `APPROVED` con **el mismo** `chargeId`. No se cobra de nuevo |
| 3 | cualquiera | `reservation_id: "no-es-guid"` | `InvalidArgument` |
| 4 | cualquiera | `amount_cents: 0` | `InvalidArgument` |
| 5 | cualquiera | `currency: "ars"` o `"USD"` | `InvalidArgument` con la lista de monedas aceptadas |
| 6 | `Decline` | Id nuevo, y después el mismo id otra vez | `DECLINED` "Fondos insuficientes", con el mismo `chargeId` en el reintento |
| 7 | `Decline` | Un id aprobado en el caso 1 | Sigue `APPROVED`: el resultado guardado no depende del modo actual |
| 8 | `Error` | Id nuevo | `Unavailable`. En la base, el cobro queda `Failed` |
| 9 | `Approve` | El id del caso 8 | `APPROVED` con el mismo `chargeId`: el `Failed` se reclama y se cobra |
| 10 | — | Payment detenido | Conexión rechazada. Un cliente gRPC de .NET lo recibe como `Unavailable` |
| 11 | cualquiera | El id del caso 1 con `amount_cents: 999` | `InvalidArgument` "El pedido no coincide con el cobro registrado". No se cobra, y el log de Payment muestra los dos montos. (Con otra moneda el rechazo llega antes, como en el caso 5, mientras `ARS` sea la única aceptada) |
| 12 | `Slow` y después `Approve` | Detené Payment a mitad de un cobro `Slow`, arrancalo en `Approve` y repetí el pedido | Antes de 60 s: `Aborted` (la fila sigue `Pending`). Después de 60 s: `APPROVED` con el mismo `chargeId`, y el log muestra "Cobro Pending abandonado" |
| 13 | cualquiera | `amount_cents: 100000001`, o un `reservation_id` con llaves `{...}` | `InvalidArgument`. No se crea ninguna fila |
| 14 | `Approve` | Id nuevo con un `x-correlation-id` de más de 64 caracteres | `APPROVED`. El log muestra `CorrelationId:invalido` |

### Caso `Slow`: el reintento de Booking

Reproduce lo que hace Booking: un deadline de 5 s y reintentos con el mismo id.

1. Arrancá Payment con `SimulatedGateway__Mode=Slow`.
2. Primera llamada con `-max-time 5`, el deadline de Booking: responde `DeadlineExceeded`.
3. Repetila enseguida, sin `-max-time`: responde `Aborted` ("Ya hay un cobro en curso"). En la base, el cobro está `Pending`.
4. Esperá a que pasen unos 15 s desde la primera llamada y repetila: responde `APPROVED` con el **mismo** `chargeId`.

Si el paso 4 todavía responde `Aborted`, la pasarela no terminó: esperá unos segundos y volvé a intentar.

## 8. Consultar la base (cotejo manual)

Así se coteja un "posible cobro tardío" que informa Booking:

```bash
docker exec postgres-db psql -U postgres -d payment_db \
  -c "select reservation_id, status, amount_cents, currency, failure_reason, id as charge_id, created_at, completed_at from charges order by created_at"
```

Por una reserva puntual:

```bash
docker exec postgres-db psql -U postgres -d payment_db \
  -c "select status, id from charges where reservation_id = '11111111-1111-4111-8111-000000000001'"
```

## 9. Volver a empezar

| Qué | Comando |
|---|---|
| Borrar los cobros y conservar la tabla | `docker exec postgres-db psql -U postgres -d payment_db -c "truncate charges"` |
| Deshacer todas las migraciones | `dotnet ef database update 0 --project src/Payment/Payment.Data --startup-project src/Payment/Payment.Grpc` |
| Detener PostgreSQL y conservar los datos | `docker compose stop postgres-db` |
| Borrar todo, incluidas las bases de los otros servicios | `docker compose down -v` |

## 10. Problemas frecuentes

| Síntoma | Causa | Solución |
|---|---|---|
| `28P01: la autentificación password falló` (en español) | Respondió un PostgreSQL de Windows en el mismo puerto | Pasá Docker al 5433 (sección 2) |
| `28P01` en inglés | La contraseña de user-secrets no coincide con la del `.env` | Volvé a ejecutar el paso 3 con la contraseña del `.env` |
| `Connection string 'Payment' is not configured` | Falta la cadena en user-secrets | Paso 3 |
| El contenedor se reinicia o el log dice `variable ... is required` | Falta alguna de las nueve variables de bases en el `.env` | Completalas y recreá el volumen |
| `grpcurl: command not found` | El `PATH` no se actualizó | Abrí una terminal nueva |
| `Failed to dial target host "localhost:5183"` | Payment no está corriendo, o arrancó con el perfil `https` | Arrancalo con el perfil `http` |
| Los logs no muestran `CorrelationId` | Falta `Logging:Console:FormatterName` o `IncludeScopes` | Revisá `appsettings.json` de Payment.Grpc |
