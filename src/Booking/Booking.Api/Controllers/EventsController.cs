using Booking.Api.Authentication;
using Booking.Business.Dtos;
using Booking.Business.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

// Expone la consulta HTTP de asientos ocupados por evento.
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly OccupiedSeatsService _occupiedSeatsService;

    public EventsController(OccupiedSeatsService occupiedSeatsService)
    {
        _occupiedSeatsService = occupiedSeatsService;
    }

    [Authorize(Policy = ServiceAuthenticationPolicies.AvailabilityService)]
    [HttpGet("{eventId}/occupied-seats")]
    [ProducesResponseType(
        typeof(OccupiedSeatsResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OccupiedSeatsResponse>> GetOccupiedSeats(
        [FromRoute] string eventId,
        CancellationToken cancellationToken)
    {
        // Valida el identificador antes de consultar Business.
        if (!Guid.TryParse(eventId, out var parsedEventId) ||
            parsedEventId == Guid.Empty)
        {
            return Problem(
                type: "about:blank",
                title: "Invalid event ID",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "The event ID must be a valid, non-empty UUID.");
        }

        var response = await _occupiedSeatsService.GetOccupiedSeatsAsync(
            parsedEventId,
            cancellationToken);

        // Una consulta sin coincidencias devuelve 200 con seatIds vacío.
        return Ok(response);
    }
}
