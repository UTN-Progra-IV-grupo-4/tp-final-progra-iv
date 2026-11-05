# Autenticación de usuarios en Availability

## Implementación

Este ticket implementa autenticación de usuarios en el sitio MVC de Availability con dos modos:

### 1. Modo Desarrollo (Mock)

- Handler de autenticación que siempre autentica un usuario fijo
- Activo solo en modo Development cuando está configurado
- Usuario configurable en `appsettings.Development.json`

### 2. Modo Producción (Keycloak)

- OpenID Connect con Keycloak
- Client `availability-web` con PKCE
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
  - Configuración de Keycloak
  - Configuración de DevelopmentAuthentication (disabled por defecto)

- `src/Availability/Availability.Web/appsettings.Development.json`
  - DevelopmentAuthentication habilitado por defecto
  - Usuario de desarrollo configurado

- `src/Availability/Availability.Web/Views/Shared/_Layout.cshtml`
  - Agregado menú de usuario con dropdown
  - Botón de logout cuando autenticado
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

### Modo Producción

En `appsettings.json`:

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

O configuración de variables de entorno:

```bash
KEYCLOAK_ISSUER=http://localhost:8080/realms/seat-reservation
KEYCLOAK_FRONTEND_CLIENT_ID=availability-web
```

## Cómo funciona

### 1. Login

- Usuario visita `/Account/Login`
- En modo desarrollo: se autentica con el handler de desarrollo
- En modo producción: se redirige a Keycloak para login OIDC
- Después de login, se crea cookie de autenticación

### 2. Logout

- En modo desarrollo: solo se elimina la cookie
- En modo producción: se hace logout de Keycloak y se elimina la cookie

### 3. Protección de endpoints

- El endpoint `/api/reservations` ya tiene `[Authorize]`
- Usuario anónimo que intenta reservar es redirigido a login
- API endpoints sin autenticación devuelven 401
- MVC páginas sin autenticación redirigen a login page

## Criterios de aceptación

- ✅ Login/logout OIDC en el sitio MVC
- ✅ Acciones de reserva protegidas con `[Authorize]`
- ✅ Usuario anónimo redirigido a login al intentar reservar
- ✅ Modo desarrollo con usuario fijo
- ✅ Modo producción con Keycloak
- ✅ Handler de desarrollo solo activo en Development
- ✅ Build compila sin errores

## Pruebas

### Modo Desarrollo

1. Iniciar Availability.Web en modo Development
2. Verificar log: `[AUTH] Using DEVELOPMENT authentication (fixed user)`
3. Navegar a `/Account/Login`
4. Hacer click en "Login (Development)"
5. Verificar que el usuario está autenticado en el navbar
6. Intentar reservar un asiento (debería funcionar)
7. Hacer logout

### Modo Producción

1. Configurar `DevelopmentAuthentication:Enabled: false`
2. Iniciar Keycloak (opcional si ya está corriendo)
3. Iniciar Availability.Web
4. Verificar log: `[AUTH] Using OPENID CONNECT with Keycloak`
5. Navegar a `/Account/Login`
6. Hacer click en "Login con Keycloak"
7. Iniciar sesión en Keycloak (user1/User123!)
8. Verificar redirección de vuelta y usuario autenticado
9. Intentar reservar un asiento
10. Hacer logout (debería redirigir a Keycloak logout)

## Importante

⚠️ **El handler de desarrollo NO puede permanecer activo en la demo.**

Antes de la demo o de mergear a producción, asegurarse de:

1. Configurar `DevelopmentAuthentication:Enabled: false` en `appsettings.json`
2. Verificar que Keycloak esté corriendo
3. Probar el flujo de login con Keycloak
4. Eliminar cualquier configuración de desarrollo en archivos de producción

## Referencias

- Issue #14 - Autenticación de usuarios en Availability
- Issue #10 - Configurar servidor de autorización (Keycloak)
- docs/booking/authentication.md - Autenticación entre servicios
