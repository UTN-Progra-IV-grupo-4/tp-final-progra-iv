using System.Security.Claims;
using Availability.Web.Application;
using Availability.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Web.Controllers;

[ApiController]
[Route("api")]
public class ReservationController : ControllerBase
{
    private readonly ReservationRequestHandler _handler;

    public ReservationController(ReservationRequestHandler handler)
    {
        _handler = handler;
    }

    [Authorize]
    [HttpPost("reservations")]
    public async Task<IActionResult> CreateReservation(
        [FromBody] ReservationRequestCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.FindFirst("sub")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "User identifier is missing from the authenticated principal." });
            }

            var reservationRequest = await _handler.HandleAsync(command, userId, cancellationToken);

            return Accepted(new
            {
                message = "solicitud recibida",
                requestId = reservationRequest.Id,
                status = reservationRequest.Status.ToString()
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
