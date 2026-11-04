# Autenticación entre microservicios

## Configuración implementada

### 1. Paquetes agregados

**Availability.Web:**
- `Microsoft.AspNetCore.Authentication.JwtBearer` - Para validación de tokens
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` - Para OIDC (login de usuarios)

**Booking.Api:**
- `Microsoft.AspNetCore.Authentication.JwtBearer` - Para validación de tokens

**Payment.Grpc:**
- `Microsoft.AspNetCore.Authentication.JwtBearer` - Para validación de tokens

### 2. Configuración de appsettings.json

**Availability.Web:**
```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "ClientId": "availability-web",
    "ServiceClientId": "availability-service",
    "ServiceClientSecret": ""
  }
}
```

**Booking.Api:**
```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "ClientId": "booking-service",
    "ClientSecret": ""
  }
}
```

**Payment.Grpc:**
```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "Audience": "payment-service"
  }
}
```

### 3. Middleware configurado

**Booking.Api (Program.cs):**
- JwtBearer authentication configurado para validar tokens
- Policy "ServiceOnly" creada para endpoints que requieren autenticación de servicio
- Logs de auditoría en OnTokenValidated, OnAuthenticationFailed, OnChallenge, OnForbidden

**Payment.Grpc (Program.cs):**
- JwtBearer authentication configurado para validar tokens
- Policy "ServiceOnly" con validación de claim "azp" = "booking-service"
- Logs de auditoría en OnTokenValidated, OnAuthenticationFailed

**Availability.Web (Program.cs):**
- OpenID Connect configurado para login de usuarios (Authorization Code + PKCE)
- Policy "Public" para endpoints públicos (consulta de asientos)
- Policy "Authenticated" para endpoints que requieren usuario autenticado
- Logs de auditoría en OnTokenValidated, OnAuthenticationFailed
- KeycloakTokenService agregado para obtener tokens de servicio

### 4. Cómo aplicar autorización cuando se agreguen endpoints

#### Booking.Api - Controller REST

Cuando se agreguen controllers, aplicar autorización así:

```csharp
[ApiController]
[Route("api/[controller]")]
public class ReservationsController : ControllerBase
{
    // Endpoint público (sin autorización)
    [HttpGet("public-info")]
    public IActionResult GetPublicInfo()
    {
        return Ok("Public info");
    }

    // Endpoint que requiere token de usuario o servicio
    [Authorize]
    [HttpGet("{id}")]
    public IActionResult GetById(Guid id)
    {
        return Ok($"Reservation {id}");
    }

    // Endpoint que solo permite servicio
    [Authorize(Policy = "ServiceOnly")]
    [HttpPost("internal")]
    public IActionResult InternalOperation()
    {
        return Ok("Internal operation");
    }
}
```

#### Payment.Grpc - Servicio gRPC

Cuando se agreguen servicios gRPC, aplicar autorización así:

```csharp
// En Program.cs:
app.MapGrpcService<PaymentService>()
    .RequireAuthorization("ServiceOnly");

// O en el servicio:
public class PaymentService : Payment.PaymentBase
{
    public override Task<PaymentResponse> ProcessPayment(
        PaymentRequest request,
        ServerCallContext context)
    {
        // El token ya fue validado por el middleware
        var clientId = context.GetHttpContext().User.FindFirst("azp")?.Value;
        Console.WriteLine($"[AUDIT] Payment requested by: {clientId}");

        // Lógica de procesamiento
        return Task.FromResult(new PaymentResponse { Success = true });
    }
}
```

#### Availability.Web - Controller MVC

```csharp
public class ReservationController : Controller
{
    // Endpoint público (consulta de asientos)
    [AllowAnonymous]
    public IActionResult Index()
    {
        return View();
    }

    // Endpoint que requiere usuario autenticado
    [Authorize(Policy = "Authenticated")]
    [HttpPost]
    public IActionResult Create(ReservationRequest request)
    {
        // Obtener token de servicio para llamar a Booking
        var tokenService = HttpContext.RequestServices.GetRequiredService<KeycloakTokenService>();
        var serviceToken = await tokenService.GetServiceTokenAsync();

        // Llamar a Booking con el token de servicio
        // ...

        return Ok();
    }
}
```

### 5. Variables de entorno

Configurar en `.env`:

```bash
KEYCLOAK_AVAILABILITY_CLIENT_SECRET=<secret-de-availability-service>
KEYCLOAK_BOOKING_CLIENT_SECRET=<secret-de-booking-service>
KEYCLOAK_PAYMENT_CLIENT_SECRET=<secret-de-payment-service>
```

O en User Secrets para desarrollo local:

```bash
cd src/Availability/Availability.Web
dotnet user-secrets set "Keycloak:ServiceClientSecret" "availability-secret-dev"

cd src/Booking/Booking.Api
dotnet user-secrets set "Keycloak:ClientSecret" "booking-secret-dev"
```

### 6. Pruebas

Cuando se agreguen endpoints, probar así:

#### Sin token (debe dar 401):
```bash
curl http://localhost:5001/api/reservations/123
```

#### Con token de servicio:
```bash
# Obtener token
TOKEN=$(curl -X POST http://localhost:8080/realms/seat-reservation/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=booking-service" \
  -d "client_secret=<secret>" | jq -r '.access_token')

# Usar token
curl http://localhost:5001/api/reservations/123 \
  -H "Authorization: Bearer $TOKEN"
```

#### gRPC con token:
```bash
grpcurl -plaintext \
  -H "Authorization: Bearer $TOKEN" \
  localhost:5003 \
  payment.Payment/ProcessPayment
```

### 7. Criterios de aceptación

- ✅ Paquetes de autenticación agregados
- ✅ Middleware configurado en los tres servicios
- ✅ Políticas de autorización definidas
- ✅ Logs de auditoría implementados
- ✅ KeycloakTokenService creado para Availability
- ⏳ Llamadas sin token son rechazadas con 401 (pendiente de implementar endpoints)
- ⏳ Llamadas Availability → Booking llevan token de servicio (pendiente de implementar)
- ⏳ Llamadas gRPC Booking → Payment llevan token de servicio (pendiente de implementar)
- ⏳ Cada API valida tokens y scopes (pendiente de implementar endpoints)

### 8. Referencias

- Issue #10 - Configurar el servidor de autorización
- Issue #11 - Autenticación entre servicios
- infra/keycloak/README.md - Documentación de Keycloak
