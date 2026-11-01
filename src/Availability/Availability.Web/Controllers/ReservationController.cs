using Availability.Web.Application;
using Availability.Web.Domain;
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

    [HttpPost("reservations")]
    public async Task<IActionResult> CreateReservation(
        [FromBody] ReservationRequestCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var reservationRequest = await _handler.HandleAsync(command, cancellationToken);

            return Accepted(new
            {
                message = "solicitud recibida",
                reservationId = reservationRequest.Id,
                status = reservationRequest.Status.ToString()
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
