using Availability.Application.ReservationRequests;
using Availability.Web.Authentication;
using Availability.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Web.Controllers;

// MVC flow of #15: seat map -> POST -> redirect -> "request received" screen. It goes through the
// same handler as POST /api/reservations, which keeps its own contract.
// Both actions are never cached: they carry the id of a request and change with every submission.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReservationRequestsController(
    ReservationRequestHandler requestHandler,
    ReservationReceiptHandler receiptHandler,
    ILogger<ReservationRequestsController> logger) : Controller
{
    public const string ErrorTempDataKey = "ReservationError";

    // AllowAnonymous so an anonymous user is sent to the login with the seat map as return URL
    // (the default challenge would return to this POST-only route). The anti-forgery token is still
    // validated first, and the handler is never reached without an authenticated user.
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [HttpPost("eventos/{eventId:guid}/reservar")]
    public async Task<IActionResult> Create(Guid eventId, Guid seatId, CancellationToken cancellationToken)
    {
        var seatMapUrl = Url.Action("Seats", "Events", new { eventId, selectedSeatId = seatId }) ?? "/";

        var userId = User.GetUserId();
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            // The return URL is built here from route values, never taken from the request.
            return RedirectToAction("Login", "Account", new { returnUrl = seatMapUrl });
        }

        // A malformed seat id fails binding: it is the same case as a seat that does not exist.
        if (!ModelState.IsValid)
        {
            return SeatNotAvailable(eventId);
        }

        try
        {
            var reservationRequest = await requestHandler.HandleAsync(
                new ReservationRequestCommand { EventId = eventId, SeatId = seatId },
                userId,
                cancellationToken);

            // Post/Redirect/Get: refreshing the received page must not send the request again.
            Response.Headers.Location = Url.Action(nameof(Received), new { requestId = reservationRequest.Id });
            return StatusCode(StatusCodes.Status303SeeOther);
        }
        catch (InvalidOperationException)
        {
            return SeatNotAvailable(eventId);
        }
        catch (ReservationRequestPublishException exception)
        {
            logger.LogError(
                exception.InnerException,
                "Reservation request {RequestId} could not be published and was removed.",
                exception.RequestId);

            // Nothing from the broker error reaches the page.
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("PublishFailed", new ReservationPublishFailedViewModel(eventId, seatId));
        }
    }

    [Authorize]
    [HttpGet("reservas/{requestId:guid}/recibida")]
    public async Task<IActionResult> Received(Guid requestId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var receipt = await receiptHandler.HandleAsync(requestId, userId, cancellationToken);
            return View(new ReservationReceivedViewModel(receipt));
        }
        catch (KeyNotFoundException)
        {
            // Missing and foreign requests are indistinguishable, as in the status page.
            return NotFound();
        }
    }

    private IActionResult SeatNotAvailable(Guid eventId)
    {
        TempData[ErrorTempDataKey] = "El asiento elegido ya no está disponible para reservar. Elegí otro asiento.";
        return RedirectToAction("Seats", "Events", new { eventId });
    }
}
