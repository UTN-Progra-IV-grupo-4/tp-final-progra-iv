# Historial de prompts — Rocío Noelia Herrera

**Componente y ticket:** Availability: vista de estado de la reserva (#16).
**Alcance:** prompts enviados al asistente de IA para implementar la vista de estado de la solicitud de reserva en Availability, en el orden en que se enviaron. Es un registro individual del uso de IA, no una metodología aprobada por el equipo.

## Prompts

### 1. Estados y cliente de Booking

“Creá en Availability.Application/Abstractions los enums de estado (Retained, Confirmed, Rejected, Expired) y de motivo de rechazo (SeatTaken, PaymentFailed, PaymentUnavailable) según el contrato, y una interfaz IBookingStatusClient con GetStatusAsync que devuelva null cuando Booking responde 404 (procesando).”

### 2. Excepción de Booking no disponible

“Creá una excepción BookingUnavailableException siguiendo el mismo estilo que ReservationRequestPublishException, para cuando Booking falla, rechaza el token o no responde. Tiene que quedar claro que no es un rechazo de la reserva.”

### 3. Cliente falso de Booking

“Hacé un cliente falso de Booking en Availability.Infrastructure que devuelva los cuatro estados y el caso 404, para poder construir la vista sin el endpoint real del #21, y registralo en DependencyInjection.”

### 4. Buscar la solicitud por Id

“Agregá GetByIdAsync al IReservationRequestRepository y su implementación con EF Core en ReservationRequestsRepository, siguiendo el estilo del código existente.”

### 5. Verificar el dueño y consultar el estado

“Creá un ReservationStatusHandler que primero verifique que la solicitud pertenece al usuario autenticado y recién después le pida el estado a Booking. Si la solicitud no existe o es de otro usuario, que responda lo mismo (404), para no revelar qué ids existen.”

### 6. Vista de estado

“Hacé una vista MVC con Bootstrap que muestre Procesando, Retenida, Confirmada, Rechazada con el motivo en castellano y Vencida, y un aviso cuando Booking no está disponible.”

### 7. Controlador

“Creá el controlador de la vista de estado tomando el usuario logueado igual que ReservationController y manejando los errores igual que ellos: 404 si no se encuentra y un aviso si Booking no responde.”

### 8. Consulta automática («procesando»)

“Agregá la política de polling que acordó el equipo: consultar cada 3 segundos hasta 2 minutos (configurable), seguir mientras esté Procesando o Retenida, parar en Confirmada, Rechazada o Vencida, y al terminar mostrar ‘todavía no tenemos respuesta’ con un botón para actualizar.”

### 9. Tests

“Escribí tests con xUnit para el handler siguiendo el estilo de ReservationRequestHandlerTests: estado propio, procesando, solicitud de otro usuario, solicitud inexistente y Booking no disponible.”

### 10. Pull request

“Completá la plantilla de PR del equipo para esta rama, usando ‘Refs #16’ porque el ticket no se cierra hasta la integración real con Booking.”
