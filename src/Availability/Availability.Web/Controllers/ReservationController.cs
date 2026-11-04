using System.Security.Claims;
using Availability.Application.ReservationRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Web.Controllers;

[ApiController]
[Route("api")]
public class ReservationController : ControllerBase
{
    private readonly ReservationRequestHandler _handler;
    private readonly ILogger<ReservationController> _logger;

    public ReservationController(ReservationRequestHandler handler, ILogger<ReservationController> logger)
    {
        _handler = handler;
        _logger = logger;
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
                // The entity has no status: every accepted request starts as Pending until Booking decides.
                status = "Pending"
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (ReservationRequestPublishException exception)
        {
            _logger.LogError(
                exception.InnerException,
                "Reservation request {RequestId} could not be published and was removed.",
                exception.RequestId);

            // The inner exception message is not exposed: it may describe broker internals.
            return Problem(
                title: "Reservation request could not be processed",
                detail: "The reservation request could not be sent for processing. Please try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
