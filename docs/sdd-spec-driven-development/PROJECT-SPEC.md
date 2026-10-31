# Especificación del sistema de reserva de asientos

> Documento de contexto personal para los tickets desarrollados por Alejandro. No representa una especificación general aprobada por el equipo ni reemplaza el enunciado, los issues o las decisiones acordadas.

## 1. Propósito

El sistema permite consultar asientos de un evento, solicitar la reserva de un único asiento, procesar su retención y pago, y consultar el resultado de manera asincrónica.

## 2. Componentes

| Componente | Responsabilidad | Arquitectura |
|---|---|---|
| Availability | Presentación, autenticación de usuario, consulta de asientos y publicación de solicitudes. | Clean Architecture, dominio anémico. |
| Booking | Consumo de solicitudes, retención, rechazo, pago, consulta de estado y vencimiento. | N-Layer, dominio anémico. |
| Payment | Procesamiento simulado e idempotente de cobros mediante gRPC. | N-Layer, dominio anémico. |
| Contracts | Mensajes y contratos compartidos aprobados. | Biblioteca compartida, sin lógica de negocio. |
| Keycloak | Login OIDC y autenticación entre servicios mediante client credentials. | Contenedor externo. |
| PostgreSQL | Persistencia independiente por servicio. | Un contenedor local con tres bases y usuarios separados. |
| Azure Service Bus | Transporte asincrónico de solicitudes de reserva. | Topic con subscription compartida por réplicas de Booking. |

El servicio de notificaciones forma parte de los diagramas, pero su implementación es opcional por decisión del profesor.

## 3. Flujo principal

```text
Usuario
  │ consulta y solicita
  ▼
Availability
  │ publica solicitud y responde inmediatamente
  ▼
Azure Service Bus
  │ entrega al menos una vez
  ▼
Booking
  │ retiene o rechaza
  │ llama por gRPC
  ▼
Payment
  │ devuelve éxito o fallo
  ▼
Booking actualiza el estado
  │
  ▼
Availability consulta y muestra el resultado
```

## 4. Invariantes globales

### SYS-INV-01 - Un asiento activo

Para un mismo evento y asiento puede existir como máximo una reserva en estado `Retained` o `Confirmed`.

### SYS-INV-02 - Una solicitud, una identidad

El identificador creado por Availability se conserva como `Reservation.Id` en Booking y como clave de idempotencia en las integraciones posteriores.

### SYS-INV-03 - Un asiento por solicitud

Cada solicitud contiene un solo evento y un solo asiento. Varias localidades requieren varias solicitudes.

### SYS-INV-04 - Persistencia por servicio

Cada microservicio solo accede a su propia base de datos. Ningún servicio consulta tablas de otro servicio.

### SYS-INV-05 - Comunicación por contrato

Los servicios se comunican únicamente mediante HTTP, gRPC o broker. No se referencian entre sí como proyectos; solamente pueden compartir `Contracts`.

### SYS-INV-06 - Entrega repetida segura

La entrega del broker es at-least-once. Procesar nuevamente el mismo mensaje no puede crear una segunda reserva ni cambiar silenciosamente su identidad.

### SYS-INV-07 - Secretos fuera del repositorio

Connection strings, tokens y contraseñas se suministran mediante User Secrets o variables de entorno y nunca se versionan.

## 5. Consistencia

Availability confirma que recibió la solicitud antes de que Booking la procese. Durante ese intervalo el sistema es eventualmente consistente y la consulta de estado puede mostrar `Processing`.

La base de Booking es la autoridad para resolver la competencia por un asiento. Los locks en memoria no coordinan réplicas diferentes.

## 6. Estados de reserva

| Estado | Significado |
|---|---|
| `Retained` | El asiento está reservado temporalmente. |
| `Confirmed` | El pago fue exitoso y la reserva quedó confirmada. |
| `Rejected` | La solicitud fue rechazada con un motivo explícito. |
| `Expired` | La retención venció y el asiento volvió a quedar disponible. |

Motivos de rechazo: `SeatTaken`, `PaymentFailed` y `PaymentUnavailable`.

## 7. Tickets que forman el flujo

```text
#15 publicar solicitud
        ↓
#18 consumir y retener/rechazar
        ├──→ #20 vencer retenciones
        └──→ #19 procesar pago ← #22 Payment gRPC
                    ↓
                 #21 estado Booking
                    ↓
                 #16 vista Availability
                    ↓
                 #25 demo final
```

Los tickets transversales #8, #9, #11 y #24 documentan arquitectura, historial de prompts, autenticación entre servicios y auditoría distribuida.
