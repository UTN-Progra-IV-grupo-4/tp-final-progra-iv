# Guía de Integración con Keycloak

Esta guía explica cómo integrar cada microservicio del sistema con Keycloak para autenticación y autorización.

## Resumen de integración

| Microservicio | Tipo de autenticación | Paquete NuGet | Configuración principal |
|---------------|----------------------|---------------|-------------------------|
| **Availability.Web** | OIDC (Authorization Code + PKCE) | `Microsoft.AspNetCore.Authentication.OpenIdConnect` | OpenIDConnect middleware |
| **Booking.Api** | JWT validation (Client Credentials) | `Microsoft.AspNetCore.Authentication.JwtBearer` | JwtBearer middleware |
| **Payment.Grpc** | JWT validation (Client Credentials) | `Microsoft.AspNetCore.Authentication.JwtBearer` + gRPC interceptor | JwtBearer middleware + gRPC interceptor |

---

## 1. Availability.Web (Frontend MVC)

### Objetivo

- Permitir que los usuarios se autentiquen vía Keycloak
- Redirigir usuarios a Keycloak para login
- Validar tokens en endpoints protegidos (reservas)
- Permitir acceso público a consulta de disponibilidad

### Instalación de dependencias

```bash
dotnet add src/Availability/Availability.Web/Availability.Web.csproj package Microsoft.AspNetCore.Authentication.OpenIdConnect
dotnet add src/Availability/Availability.Web/Availability.Web.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
```

### Configuración en `appsettings.json`

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "ClientId": "availability-web",
    "ClientSecret": "",
    "ResponseType": "code",
    "SaveTokens": true,
    "RequireHttpsMetadata": false,
    "GetClaimsFromUserInfoEndpoint": true,
    "TokenValidationParameters": {
      "NameClaimType": "name",
      "RoleClaimType": "roles"
    }
  }
}
```

### Configuración en `Program.cs`

```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configuración de Keycloak
var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
    ?? "http://localhost:8080/realms/seat-reservation";
var keycloakClientId = builder.Configuration["Keycloak:ClientId"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_FRONTEND_CLIENT_ID")
    ?? "availability-web";

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Authority = keycloakAuthority;
    options.ClientId = keycloakClientId;
    options.ResponseType = "code";
    options.UsePkce = true; // PKCE para mayor seguridad
    options.SaveTokens = true;
    options.RequireHttpsMetadata = false; // Deshabilitar para desarrollo
    options.GetClaimsFromUserInfoEndpoint = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "name",
        RoleClaimType = "roles"
    };

    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    options.Events = new OpenIdConnectEvents
    {
        OnRedirectToIdentityProvider = context =>
        {
            // Personalizar redirect si es necesario
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            // Log de autenticación exitosa (auditoría)
            var userId = context.Principal?.FindFirst("sub")?.Value;
            Console.WriteLine($"[AUTH] Usuario autenticado: {userId}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            // Log de error de autenticación (auditoría)
            Console.WriteLine($"[AUTH ERROR] {context.Exception.Message}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // Endpoint público: consulta de disponibilidad
    options.AddPolicy("Public", policy =>
    {
        policy.RequireAssertion(_ => true);
    });

    // Endpoint protegido: crear reserva
    options.AddPolicy("Authenticated", policy =>
    {
        policy.RequireAuthenticatedUser();
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Endpoints públicos
app.MapGet("/seats", ...).RequireAuthorization("Public");

// Endpoints protegidos
app.MapPost("/reservations", ...).RequireAuthorization("Authenticated");

app.Run();
```

### Endpoints necesarios

```csharp
// Login: redirige a Keycloak
app.MapGet("/login", async (HttpContext context) =>
{
    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
});

// Logout: cierra sesión en Keycloak
app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
});

// Callback: Keycloak redirige aquí después del login
// ( manejado automáticamente por OpenIDConnect middleware)
```

### Auditoría

Log de eventos de autenticación en `OnTokenValidated` y `OnAuthenticationFailed`:

```csharp
options.Events.OnTokenValidated = context =>
{
    var userId = context.Principal?.FindFirst("sub")?.Value;
    var timestamp = DateTime.UtcNow;
    Console.WriteLine($"[AUDIT] Login exitoso - User: {userId}, Time: {timestamp}");
    return Task.CompletedTask;
};
```

---

## 2. Booking.Api (REST API)

### Objetivo

- Obtener tokens de servicio usando Client Credentials
- Validar tokens de usuarios en endpoints protegidos
- Llamar a Payment con token de servicio
- Emitir tokens para otros servicios (si es necesario)

### Instalación de dependencias

```bash
dotnet add src/Booking/Booking.Api/Booking.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Booking/Booking.Api/Booking.Api.csproj package System.Net.Http.Json
```

### Configuración en `appsettings.json`

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "ClientId": "booking-service",
    "ClientSecret": "booking-service-secret-change-in-production",
    "RequireHttpsMetadata": false
  },
  "Payment": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "ClientId": "booking-service",
    "ClientSecret": "booking-service-secret-change-in-production",
    "TargetAudience": "payment-service"
  }
}
```

### Configuración en `Program.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
    ?? "http://localhost:8080/realms/seat-reservation";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.Authority = keycloakAuthority;
    options.RequireHttpsMetadata = false;
    options.Audience = "booking-service";

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = keycloakAuthority,
        ValidAudiences = new[] { "booking-service" }
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            // Log de validación exitosa (auditoría)
            var userId = context.Principal?.FindFirst("sub")?.Value;
            var clientId = context.Principal?.FindFirst("client_id")?.Value;
            Console.WriteLine($"[AUTH] Token válido - User: {userId}, Client: {clientId}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            // Log de error de autenticación (auditoría)
            Console.WriteLine($"[AUTH ERROR] {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            // Log de intento sin token (auditoría)
            Console.WriteLine($"[AUTH] Challenge - 401 Unauthorized");
            return Task.CompletedTask;
        },
        OnForbidden = context =>
        {
            // Log de acceso denegado (auditoría)
            Console.WriteLine($"[AUTH] Forbidden - 403");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServiceOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("client_id", "booking-service");
    });
});

// Servicio para obtener tokens de servicio
builder.Services.AddScoped<TokenService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Endpoints que requieren autenticación de usuario
app.MapPost("/reservations", ...).RequireAuthorization();

// Endpoints internos entre servicios
app.MapPost("/internal/reservations", ...).RequireAuthorization("ServiceOnly");

app.Run();
```

### Servicio para obtener tokens (Client Credentials)

```csharp
public class TokenService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public TokenService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> GetServiceTokenAsync()
    {
        var authority = _configuration["Payment:Authority"];
        var clientId = _configuration["Payment:ClientId"];
        var clientSecret = _configuration["Payment:ClientSecret"];

        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "scope", "openid" }
        };

        var response = await _httpClient.PostAsync(
            $"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(parameters));

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return content?.AccessToken ?? throw new InvalidOperationException("No access token received");
    }

    private record TokenResponse(string AccessToken, int ExpiresIn, string TokenType);
}
```

### Llamar a Payment con token

```csharp
public class PaymentClient
{
    private readonly TokenService _tokenService;
    private readonly Payment.PaymentClient _grpcClient;

    public PaymentClient(TokenService tokenService, Payment.PaymentClient grpcClient)
    {
        _tokenService = tokenService;
        _grpcClient = grpcClient;
    }

    public async Task<PaymentResponse> ProcessPaymentAsync(PaymentRequest request)
    {
        var token = await _tokenService.GetServiceTokenAsync();

        var headers = new Metadata
        {
            { "Authorization", $"Bearer {token}" }
        };

        return await _grpcClient.ProcessPaymentAsync(request, headers);
    }
}
```

### Auditoría

Logs en `OnTokenValidated`, `OnAuthenticationFailed`, `OnChallenge`, `OnForbidden`.

---

## 3. Payment.Grpc (gRPC API)

### Objetivo

- Validar tokens recibidos de Booking
- Rechazar llamadas sin token válido
- Registrar intentos de acceso (auditoría)

### Instalación de dependencias

```bash
dotnet add src/Payment/Payment.Grpc/Payment.Grpc.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Payment/Payment.Grpc/Payment.Grpc.csproj package Grpc.AspNetCore.Server
```

### Configuración en `appsettings.json`

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/seat-reservation",
    "RequireHttpsMetadata": false,
    "Audience": "payment-service"
  }
}
```

### Configuración en `Program.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? Environment.GetEnvironmentVariable("KEYCLOAK_ISSUER")
    ?? "http://localhost:8080/realms/seat-reservation";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.Authority = keycloakAuthority;
    options.RequireHttpsMetadata = false;
    options.Audience = "payment-service";

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = keycloakAuthority,
        ValidAudiences = new[] { "payment-service" }
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var clientId = context.Principal?.FindFirst("client_id")?.Value;
            Console.WriteLine($"[AUTH] Token válido - Client: {clientId}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"[AUTH ERROR] {context.Exception.Message}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServiceOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("client_id", "booking-service");
    });
});

// Interceptor gRPC para validación de tokens
builder.Services.AddSingleton<JwtTokenValidatorInterceptor>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<PaymentService>()
    .RequireAuthorization("ServiceOnly");

app.Run();
```

### Interceptor gRPC para validación de tokens

```csharp
public class JwtTokenValidatorInterceptor : Interceptor
{
    private readonly ILogger<JwtTokenValidatorInterceptor> _logger;

    public JwtTokenValidatorInterceptor(ILogger<JwtTokenValidatorInterceptor> logger)
    {
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var token = context.RequestHeaders.GetValue("Authorization")?.Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("[AUTH] gRPC call sin token de autorización");
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Token requerido"));
        }

        // Validación adicional si es necesaria
        _logger.LogInformation($"[AUTH] gRPC call con token de servicio");

        return base.UnaryServerHandler(request, context, continuation);
    }
}
```

### Servicio gRPC con autorización

```csharp
public class PaymentService : Payment.PaymentBase
{
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(ILogger<PaymentService> logger)
    {
        _logger = logger;
    }

    public override async Task<PaymentResponse> ProcessPayment(
        PaymentRequest request,
        ServerCallContext context)
    {
        var clientId = context.GetHttpContext().User.FindFirst("client_id")?.Value;
        _logger.LogInformation($"[AUDIT] Payment procesado por service: {clientId}");

        // Lógica de procesamiento de pago
        return new PaymentResponse
        {
            Success = true,
            TransactionId = Guid.NewGuid().ToString()
        };
    }
}
```

### Auditoría

Logs en `OnTokenValidated`, `OnAuthenticationFailed` y en el interceptor gRPC.

---

## Variables de entorno comunes

Todos los servicios deben soportar estas variables de entorno:

```bash
KEYCLOAK_ISSUER=http://localhost:8080/realms/seat-reservation
KEYCLOAK_FRONTEND_CLIENT_ID=availability-web
KEYCLOAK_BOOKING_CLIENT_ID=booking-service
KEYCLOAK_BOOKING_CLIENT_SECRET=booking-service-secret-change-in-production
KEYCLOAK_PAYMENT_CLIENT_ID=payment-service
KEYCLOAK_PAYMENT_CLIENT_SECRET=payment-service-secret-change-in-production
```

---

## Testing de integración

### 1. Probar autenticación de usuario (Availability.Web)

```bash
# 1. Iniciar Availability.Web
dotnet run --project src/Availability/Availability.Web

# 2. Navegar a http://localhost:5000
# 3. Intentar reservar (debería redirigir a Keycloak)
# 4. Loguearse con user1 / User123!
# 5. Debería obtener token y poder reservar
```

### 2. Probar Client Credentials (Booking -> Payment)

```bash
# Obtener token de servicio
curl -X POST http://localhost:8080/realms/seat-reservation/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=booking-service" \
  -d "client_secret=booking-service-secret-change-in-production"

# Usar token para llamar a Payment
grpcurl -plaintext \
  -authority "http://localhost:8080/realms/seat-reservation" \
  -H "Authorization: Bearer <TOKEN>" \
  localhost:5003 \
  payment.Payment/ProcessPayment
```

### 3. Verificar token expirado

```bash
# Usar un token expirado (esperar 1 hora)
# Debería recibir 401 Unauthorized
```

---

## Checklist de integración

Para cada microservicio:

- [ ] Instalar paquetes NuGet necesarios
- [ ] Configurar `appsettings.json` con Authority y ClientId
- [ ] Agregar middleware de autenticación en `Program.cs`
- [ ] Configurar políticas de autorización
- [ ] Implementar logs de auditoría
- [ ] Probar login/autenticación
- [ ] Probar validación de tokens
- [ ] Probar tokens expirados
- [ ] Probar tokens inválidos
- [ ] Documentar configuración

---

## Referencias

- [ASP.NET Core Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/)
- [JWT Bearer Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/jwt)
- [OpenID Connect with ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/opensiddisconnect)
- [gRPC Authentication in .NET](https://docs.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz)
