# Autenticación entre servicios (#11)

Cada llamada entre servicios lleva un token de Keycloak obtenido con *client credentials*. Quien recibe valida el token; quien llama lo pide, lo guarda en memoria y lo renueva antes de que venza.

| Llamada | Quien llama (cliente Keycloak) | Quien valida | Audiencia | `azp` permitido |
| --- | --- | --- | --- | --- |
| Availability a Booking (HTTP) | `availability-service` | Booking (#45) | `booking-service` | `availability-service` |
| Booking a Payment (gRPC) | `booking-service` | Payment | `payment-service` | `booking-service` |

No hay modo simulado: sin Keycloak configurado no se emiten tokens ni se acepta ninguno.

## Validación entrante

Booking (ver [docs/booking/authentication.md](booking/authentication.md)) y Payment usan el mismo cableado: firma, emisor, audiencia y vencimiento del JWT, más una política que exige `azp == AllowedClientId`. El código vive en `Authentication/` de cada API (`AddServiceAuthentication`).

Payment, sección `Authentication`:

| Clave | Variable de entorno | Valor por defecto |
| --- | --- | --- |
| `Authentication:Authority` | `Authentication__Authority` | vacío (fallo cerrado) |
| `Authentication:Audience` | `Authentication__Audience` | `payment-service` |
| `Authentication:AllowedClientId` | `Authentication__AllowedClientId` | `booking-service` |
| `Authentication:RequireHttpsMetadata` | `Authentication__RequireHttpsMetadata` | `true` |

La política se aplica al servicio gRPC (`MapGrpcService<PaymentGrpcService>().RequireAuthorization(...)`):

- Sin token, inválido, vencido o con otra audiencia: `UNAUTHENTICATED`.
- Token válido para Payment pero de otro cliente: `PERMISSION_DENIED`.
- `Authority` vacío: la aplicación arranca con una advertencia y todo se rechaza con `UNAUTHENTICATED`. Nunca queda abierto.

## Obtención de tokens (salientes)

La librería `src/ServiceAuthentication` la usan Availability y Booking. Se registra con `AddServiceTokenProvider(configuration)` y se conecta a un cliente con `.AddServiceBearerToken()`; sirve tanto para `HttpClient` como para clientes gRPC registrados con `AddGrpcClient`, porque ambos pasan por el pipeline de `HttpClient`.

Sección `ServiceCredentials` de cada servicio que llama:

| Clave | Variable de entorno | Valor por defecto |
| --- | --- | --- |
| `ServiceCredentials:Authority` | `ServiceCredentials__Authority` | vacío |
| `ServiceCredentials:ClientId` | `ServiceCredentials__ClientId` | `availability-service` en Availability, `booking-service` en Booking |
| `ServiceCredentials:ClientSecret` | `ServiceCredentials__ClientSecret` | vacío, **nunca se versiona** |
| `ServiceCredentials:RequireHttpsMetadata` | `ServiceCredentials__RequireHttpsMetadata` | `true` |
| `ServiceCredentials:RefreshSkewSeconds` | `ServiceCredentials__RefreshSkewSeconds` | `30` |
| `ServiceCredentials:RequestTimeoutSeconds` | `ServiceCredentials__RequestTimeoutSeconds` | `10` |

El secreto es el mismo valor que `KEYCLOAK_AVAILABILITY_CLIENT_SECRET` o `KEYCLOAK_BOOKING_CLIENT_SECRET` del `.env` con el que se importa el realm. En desarrollo:

```bash
cd src/Availability/Availability.Web
dotnet user-secrets set "ServiceCredentials:ClientSecret" "<KEYCLOAK_AVAILABILITY_CLIENT_SECRET>"

cd ../../Booking/Booking.Api
dotnet user-secrets set "ServiceCredentials:ClientSecret" "<KEYCLOAK_BOOKING_CLIENT_SECRET>"
```

`appsettings.Development.json` apunta a `http://localhost:8080/realms/seat-reservation` con `RequireHttpsMetadata` en `false`. Fuera de desarrollo la `Authority` debe ser HTTPS: el secreto viaja en el pedido de token y el proveedor se niega a enviarlo por HTTP.

Comportamiento:

- El token se guarda en memoria y se reutiliza hasta `RefreshSkewSeconds` antes de vencer (como máximo la mitad de su vida). Con llamadas concurrentes hay un único pedido a Keycloak.
- Si falta configuración o Keycloak falla, se lanza `ServiceTokenAcquisitionException` (hereda de `HttpRequestException`) y la llamada saliente no se envía; nunca se envía sin token ni con un token de usuario.
- No se registran secretos ni tokens: los logs solo traen el cliente y el código HTTP.

### Uso desde Availability

`IBookingServiceTokenProvider` (#47) debe devolver el `access_token` de `availability-service`. Con esta librería la implementación es un adaptador de una línea sobre `IServiceTokenProvider.GetAccessTokenAsync(cancellationToken)`, que ya devuelve el token sin prefijo `Bearer`.

### Uso desde Booking

Cuando exista el cliente gRPC de Payment (#24):

```csharp
builder.Services
    .AddGrpcClient<PaymentService.PaymentServiceClient>(options => options.Address = paymentAddress)
    .AddServiceBearerToken();
```

## Comprobación manual contra Keycloak

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/seat-reservation/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=booking-service \
  -d client_secret=<KEYCLOAK_BOOKING_CLIENT_SECRET> | python -c "import json,sys; print(json.load(sys.stdin)['access_token'])")

grpcurl -plaintext -H "Authorization: Bearer $TOKEN" -d '{}' \
  localhost:5088 payment.PaymentService/Charge
```

Resultado esperado: con el token de `booking-service` el pedido llega al servicio (`INVALID_ARGUMENT` por el cuerpo vacío); sin token o con el de `availability-service` o `payment-service`, `UNAUTHENTICATED`.

## Fuera de alcance

El inicio de sesión de usuarios con OpenID Connect en Availability es el #14. El esquema por defecto de Availability sigue siendo la cookie (responde 401/403 en vez de redirigir).
