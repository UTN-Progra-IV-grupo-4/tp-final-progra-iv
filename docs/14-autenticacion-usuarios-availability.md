# Autenticación de usuarios en Availability

## Implementación

Este ticket implementa autenticación de usuarios en el sitio MVC de Availability con dos modos:

### 1. Modo Desarrollo (Mock)

- Handler de autenticación que siempre autentica un usuario fijo
- Activo solo en Development y solo con `DevelopmentAuthentication:Enabled=true` (por defecto `false` en `appsettings.json`; `true` solo en `appsettings.Development.json`)
- Si `Enabled=true` en cualquier otro entorno, la aplicación **no arranca** (`InvalidOperationException`); el handler además rechaza autenticar fuera de Development
- Usuario configurable en `appsettings.Development.json`

### 2. Modo Producción (Keycloak)

- OpenID Connect con Keycloak (flujo authorization code + PKCE S256)
- Client `availability-web`: **público** (sin client secret), por eso no hay ningún secreto que configurar en la aplicación web
- Cookie de sesión como esquema por defecto; OIDC solo se invoca desde `AccountController` (login/logout de páginas MVC)
- Realm `seat-reservation` (configurado en issue #10)

## Archivos creados/modificados

### Nuevos archivos

- `src/Availability/Availability.Web/Authentication/DevelopmentAuthenticationHandler.cs`
  - Handler de autenticación para desarrollo
  - Usuario fijo configurado en appsettings

- `src/Availability/Availability.Web/Controllers/AccountController.cs`
  - Login/logout endpoints
  - Soporta ambos modos (desarrollo y producción)

- `src/Availability/Availability.Web/Views/Account/Login.cshtml`
  - Vista de login

- `src/Availability/Availability.Web/Views/Account/AccessDenied.cshtml`
  - Vista de acceso denegado

### Archivos modificados

- `src/Availability/Availability.Web/Program.cs`
  - Configuración de autenticación condicional
  - OIDC para producción, Development handler para desarrollo
  - Redirección diferenciada para API (401) vs MVC (login page)

- `src/Availability/Availability.Web/Availability.Web.csproj`
  - Agregado `Microsoft.AspNetCore.Authentication.OpenIdConnect`

- `src/Availability/Availability.Web/appsettings.json`
  - Configuración de Keycloak (`Authority`, `ClientId`)
  - `DevelopmentAuthentication:Enabled=false` (el usuario fijo vive solo en `appsettings.Development.json`)

- `src/Availability/Availability.Web/appsettings.Development.json`
  - DevelopmentAuthentication habilitado por defecto
  - Usuario de desarrollo configurado

- `src/Availability/Availability.Web/Views/Shared/_Layout.cshtml`
  - Agregado menú de usuario con dropdown
  - Botón de logout (formulario POST con anti-forgery) cuando autenticado
  - Botón de login cuando no autenticado

## Configuración

### Modo Desarrollo

En `appsettings.Development.json`:

```json
{
  "DevelopmentAuthentication": {
    "Enabled": true,
    "UserId": "dev-user-123",
    "UserName": "Development User",
    "Email": "dev@example.com"
  }
}
```

### Modo Producción (Keycloak)

En `appsettings.json` (o variables de entorno `Keycloak__Authority`, `Keycloak__ClientId`):

```json
{
  "DevelopmentAuthentication": {
    "Enabled": false
  },
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "ClientId": "availability-web"
  }
}
```

También se aceptan `KEYCLOAK_ISSUER` y `KEYCLOAK_FRONTEND_CLIENT_ID` como fallback.

- **Client secret:** no hay. `availability-web` es un client público (ver `infra/keycloak/realm-export.json`) y la seguridad del code flow la da PKCE. No se configura ni se guarda ningún secreto para el login de usuarios. Si algún día se pasa a un client confidencial, el secreto debe ir en user-secrets (`dotnet user-secrets set Keycloak:ClientSecret ...`) o en una variable de entorno, nunca en el repositorio, y hay que agregar su lectura en `Program.cs`.
- **HTTPS:** fuera de Development la `Authority` debe ser HTTPS (`RequireHttpsMetadata` es `true`). Para un Keycloak HTTP en un entorno que no sea Development hay que indicarlo con `Keycloak:RequireHttpsMetadata=false` (no recomendado).
- **Redirect URIs** registradas para el client: `http://localhost:5212/signin-oidc` y `https://localhost:7057/signin-oidc`; post-logout: `.../signout-callback-oidc`. Otro host o puerto requiere actualizar el realm.
- Las credenciales de servicio (`ServiceCredentials:*`, #51) son independientes de este login.

## Cómo funciona

### 1. Login

- Usuario visita `/Account/Login` y envía el formulario (POST con anti-forgery)
- En modo desarrollo: se autentica con el handler de desarrollo (usuario fijo de configuración; nada del request define la identidad)
- En modo producción: se redirige a Keycloak para login OIDC
- Después de login, se crea la cookie de autenticación
- `returnUrl` solo se acepta si es una URL local (`Url.IsLocalUrl`); en otro caso se vuelve a `/`
- El identificador de usuario que leen `ReservationController` y `ReservationStatusController` sale del claim `sub` o, si el mapeo de claims de ASP.NET lo renombró, de `NameIdentifier` (con Keycloak es el id del usuario del realm; el usuario de desarrollo emite los dos claims con el mismo valor)
- La cookie guarda los tokens (`SaveTokens`) porque el `id_token` se usa como `id_token_hint` al cerrar sesión; por eso es grande (varios KB, en chunks)
- Usuarios de ejemplo del realm: `user1` / `user2`, contraseña `User123!`, con nombre y apellido para que el primer login no pida completar el perfil (`VERIFY_PROFILE`). Keycloak importa el realm solo cuando está vacío: si tu volumen ya tenía el realm, recrealo con `docker compose down -v` (o cargá nombre y apellido a mano en Users) para ver el cambio

### 2. Logout

- Es un POST con anti-forgery (no existe un GET que cierre sesión)
- En modo desarrollo: solo se elimina la cookie
- En modo producción: se elimina la cookie y se redirige al end-session de Keycloak (con `id_token_hint`), que termina la sesión de Keycloak y vuelve a `/signout-callback-oidc` y luego a Home

### 3. Protección de endpoints

- El endpoint `/api/reservations` ya tiene `[Authorize]`
- `/api/*` sin sesión devuelve 401 (y 403 sin permisos), nunca una redirección a login
- Las páginas MVC con `[Authorize]` (p. ej. `/reservas/{id}/estado`) redirigen a `/Account/Login?ReturnUrl=...`

## Criterios de aceptación

- ✅ Login/logout OIDC en el sitio MVC
- ✅ Acciones de reserva protegidas con `[Authorize]`
- ✅ Usuario anónimo redirigido a login al intentar reservar
- ✅ Modo desarrollo con usuario fijo
- ✅ Modo producción con Keycloak
- ✅ Handler de desarrollo solo activo en Development
- ✅ Build compila sin errores

## Pruebas

Automáticas: `tests/Availability/Availability.Tests/UserAuthenticationTests.cs` (handler solo en Development, fallo de arranque fuera de Development, `/api/*` con 401, challenge OIDC con PKCE, `returnUrl` local, logout solo POST y fin de sesión en Keycloak).

### Modo Desarrollo

1. Iniciar Availability.Web en modo Development
2. Navegar a `/Account/Login`
3. Hacer click en "Login (Development)"
4. Verificar que el usuario está autenticado en el navbar
5. Reservar un asiento (`POST /api/reservations`): se guarda con `user_id = DevelopmentAuthentication:UserId`
6. Hacer logout

### Modo Producción (Keycloak)

1. Levantar Keycloak con `docker compose up -d keycloak` (el realm se importa solo si está vacío)
2. Ejecutar Availability.Web con `DevelopmentAuthentication__Enabled=false` (en Development) o en el entorno real
3. Navegar a `/Account/Login` y hacer click en "Login con Keycloak"
4. Iniciar sesión en Keycloak (`user1` / `User123!`)
5. Verificar la redirección de vuelta y el usuario en el navbar
6. Reservar un asiento: `reservation_requests.user_id` es el id del usuario en Keycloak
7. Hacer logout: se redirige a Keycloak y la sesión de Keycloak termina (el siguiente login vuelve a pedir contraseña)

## Importante

⚠️ **El handler de desarrollo no existe fuera de Development.**

- `appsettings.json` trae `DevelopmentAuthentication:Enabled=false`; el usuario fijo solo está en `appsettings.Development.json`
- Si `Enabled=true` llega por variable de entorno o archivo a un entorno que no es Development, la aplicación falla al arrancar
- Antes de una demo: confirmar que el entorno no es Development, que Keycloak es accesible por HTTPS y probar el flujo de login

## Referencias

- Issue #14 - Autenticación de usuarios en Availability
- Issue #10 - Configurar servidor de autorización (Keycloak)
- docs/booking/authentication.md - Autenticación entre servicios
