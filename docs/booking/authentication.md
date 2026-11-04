# Autenticación entre servicios en Booking

`GET /api/reservations/{requestId}/status` y `GET /api/events/{eventId}/occupied-seats` exigen un token JWT de Keycloak (`Authorization: Bearer <token>`). Solo se acepta un token emitido al cliente `availability-service`.

## Configuración

| Clave | Variable de entorno | Valor por defecto |
| --- | --- | --- |
| `Authentication:Authority` | `Authentication__Authority` | vacío (ver "Fallo cerrado") |
| `Authentication:Audience` | `Authentication__Audience` | `booking-service` |
| `Authentication:AllowedClientId` | `Authentication__AllowedClientId` | `availability-service` |
| `Authentication:RequireHttpsMetadata` | `Authentication__RequireHttpsMetadata` | `true` |

En desarrollo, `appsettings.Development.json` apunta a `http://localhost:8080/realms/seat-reservation` con `RequireHttpsMetadata` en `false`. `Authority` debe coincidir exactamente con el `iss` del token (URL del realm tal como la ve quien pide el token). No se versionan secretos.

## Política `AvailabilityService`

Booking valida firma, emisor (`Authority`), audiencia (`Audience`) y vencimiento. La política `AvailabilityService` exige además un usuario autenticado cuyo claim `azp` sea `AllowedClientId`.

- Sin token, token inválido, vencido o con otra audiencia: 401.
- Token válido de otro cliente (por ejemplo `payment-service`): 403.

## Fallo cerrado

Si `Authority` está vacío la aplicación arranca (el consumidor sigue funcionando) y registra una advertencia, pero todo endpoint protegido responde 401. Nunca queda abierto ni responde 500.

## Ejemplo con curl

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/seat-reservation/protocol/openid-connect/token \
  -d grant_type=client_credentials \
  -d client_id=availability-service \
  -d client_secret=<availability-service-secret> | python -c "import json,sys; print(json.load(sys.stdin)['access_token'])")

curl -H "Authorization: Bearer $TOKEN" http://localhost:<booking-port>/api/events/<event-uuid>/occupied-seats
curl -H "Authorization: Bearer $TOKEN" http://localhost:<booking-port>/api/reservations/<request-uuid>/status
```

## Reutilización

#11 reutiliza este mismo cableado (`AddServiceAuthentication` y la política por cliente) para proteger Payment.
