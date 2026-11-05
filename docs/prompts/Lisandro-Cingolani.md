# Historial de prompts — Lisandro Cingolani

**Componente:** Booking REST API: consulta del estado de una solicitud de reserva y consulta de asientos ocupados por evento (contratos, servicios, controladores, repositorio y pruebas).
**Herramienta de IA usada:** no indicada en el archivo original.
**Alcance:** prompts enviados al asistente de IA para implementar esos componentes, en el orden en que se enviaron. Es un registro individual del uso de IA, no una metodología aprobada por el equipo.

## Prompts

1. Definí el contrato de GET /api/reservations/{requestId}/status con su JSON, los estados de reserva, los motivos de rechazo y las respuestas de error. Documentalo en inglés, de forma breve y con un diagrama Mermaid en docs/contracts/reservation-status.md. Prepará este contrato para un PR separado.

2. Definí ReservationStatusResponse en Booking.Business/Dtos con RequestId, Status y RejectionReason. El identificador debe ser Guid, el estado debe ser texto y el motivo debe permitir null.

3. Agregá a ReservationRepository una consulta de reserva por identificador. Debe devolver null si no existe y consultar sin modificar datos. Incluí el registro del repositorio en la inyección de dependencias.

4. Implementá ReservationStatusService para consultar el repositorio y devolver ReservationStatusResponse. Convertí los estados y motivos a texto y verificá que una reserva rechazada tenga motivo. Registrá el servicio en Business.

5. Implementá ReservationsController para consultar el estado por requestId. Devolvé 200 con la respuesta, 400 si el identificador es inválido y 404 si la reserva no existe. Usá Problem Details para los errores.

6. Revisá qué falta de los issues #10 y #11 para proteger las consultas de Booking con un token de servicio. Identificá qué configuración ya está disponible y qué depende de otros integrantes.

7. Revisá los comentarios del PR #33 y ajustá el contrato de estado. Aclará que Availability muestra «Procesando» si Booking devuelve 404 para una solicitud que Availability ya registró.

8. Ajustá ReservationStatusService para devolver Expired cuando una reserva Retained llegue a su ExpiresAt. Usá TimeProvider para consultar la hora UTC y conservá Retained si no tiene vencimiento. El cálculo debe afectar la respuesta sin cambiar la reserva guardada.

9. Incorporá el nuevo requisito del #21 sobre asientos ocupados por evento. Definí el contrato de GET /api/events/{eventId}/occupied-seats en docs/contracts/occupied-seats.md, breve, en inglés y con Mermaid. La respuesta debe incluir eventId y seatIds para que Availability la use en el #13.

10. Agregá al repositorio la consulta de asientos ocupados de un evento. Incluí reservas Confirmed y Retained vigentes o sin vencimiento. Excluí las vencidas y devolvé los identificadores de los asientos sin duplicados.

11. Definí OccupiedSeatsResponse e implementá OccupiedSeatsService. El servicio debe tomar la hora con TimeProvider, consultar los asientos ocupados y devolver el evento y su lista de asientos. Registralo en Business.

12. Implementá EventsController para exponer la consulta de asientos ocupados. Validá eventId y devolvé 400 si es inválido. Si la consulta es válida, devolvé 200 con la lista, aunque esté vacía.

13. Completá los ajustes de los contratos del PR #33. Aclará que Availability comprueba que la solicitud pertenezca al usuario y que Booking recibe el token del servicio. Actualizá el diagrama y verificá que el PR contenga solo los dos contratos.

14. Aplicá la observación del PR #45 sobre el uso de IReservationRepository. Agregá las consultas a la interfaz y usala en ambos servicios de Business. Ajustá el registro de dependencias y los repositorios de prueba, y comprobá que el proyecto compile.

15. Agregá pruebas de ReservationStatusService usando una hora fija. Cubrí una retención justo antes, en el momento y después del vencimiento, además de una sin fecha. Verificá que no cambie el estado guardado y que una reserva Rejected sin motivo genere un error.

16. Agregá pruebas de la consulta de asientos ocupados con PostgreSQL. Comprobá qué asientos devuelve para cada estado y vencimiento, que excluya los de otro evento y que responda con una lista vacía cuando corresponda. Ejecutá las pruebas antes de cerrar el cambio.
