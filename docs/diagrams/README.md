# Diagramas

Diagramas de arquitectura del sistema de reservas, escritos en Mermaid: GitHub los renderiza directamente y quedan versionados con el código.

## Arquitectura

| Diagrama | Qué muestra |
| --- | --- |
| [system.md](system.md) | Arquitectura del sistema con el servicio de notificaciones (punto 3.h, no implementado) y el tramo de consistencia eventual (punto 3.g). |
| [availability-architecture.md](availability-architecture.md) | Clean Architecture de Availability: capas, dirección de las dependencias, puertos y adaptadores. |
| [booking-architecture.md](booking-architecture.md) | N-Layer de Booking: `Api`, `Business` y `Data`, consumidor del broker, cliente gRPC y job de vencimiento. |
| [payment-architecture.md](payment-architecture.md) | N-Layer de Payment: servicio gRPC, cobro idempotente, pasarela simulada y estados del cobro. |
| [happy-path-and-failure.md](happy-path-and-failure.md) | Secuencias de la demo: camino feliz y los cuatro caminos de falla, con el correlation id. |

## Contratos entre servicios

Cada contrato incluye su propio diagrama de secuencia y vive en [`docs/contracts/`](../contracts/):

| Contrato | Entre | Medio |
| --- | --- | --- |
| [reservation-request.md](../contracts/reservation-request.md) | Availability a Booking | Mensaje por el broker |
| [reservation-status.md](../contracts/reservation-status.md) | Availability a Booking | HTTP, estado de una solicitud |
| [occupied-seats.md](../contracts/occupied-seats.md) | Availability a Booking | HTTP, asientos ocupados de un evento |
| [payment.md](../contracts/payment.md) | Booking a Payment | gRPC, cobro de una reserva |
