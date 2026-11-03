using Booking.Business.Dtos;
using Booking.Business.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

// Traduce solicitudes HTTP a operaciones de Business.
[ApiController]
[Route("api/reservations")]
public class ReservationsController : ControllerBase
{
    private readonly ReservationStatusService _reservationStatusService;

    // Recibe el servicio mediante inyección de dependencias.
    public ReservationsController(
        ReservationStatusService reservationStatusService)
    {
        _reservationStatusService = reservationStatusService;
    }

    // TODO(#11): exigir un token de servicio con permiso para consultar Booking
    [HttpGet("{requestId}/status")]
    [ProducesResponseType(
        typeof(ReservationStatusResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationStatusResponse>> GetStatus(
        [FromRoute] string requestId,
        CancellationToken cancellationToken)
    {
        // Valida el identificador antes de consultar Business.
        if (!Guid.TryParse(requestId, out var parsedRequestId) ||
            parsedRequestId == Guid.Empty)
        {
            return Problem(
                type: "about:blank",
                title: "Invalid request ID",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "The request ID must be a valid, non-empty UUID.");
        }

        var response = await _reservationStatusService.GetStatusAsync(
            parsedRequestId,
            cancellationToken);

        // Una reserva inexistente se representa como HTTP 404.
        if (response is null)
        {
            return Problem(
                type: "about:blank",
                title: "Reservation not found",
                statusCode: StatusCodes.Status404NotFound,
                detail: "No reservation was found for the provided request ID.");
        }

        return Ok(response);
    }
}
