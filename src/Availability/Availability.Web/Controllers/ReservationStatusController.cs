using System.Security.Claims;
using Availability.Application.Abstractions;
using Availability.Application.ReservationRequests;
using Availability.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Availability.Web.Controllers;
public class ReservationStatusController : Controller
{
    private readonly ReservationStatusHandler _handler;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReservationStatusController> _logger;
    public ReservationStatusController(
        ReservationStatusHandler handler,
        IConfiguration configuration,
        ILogger<ReservationStatusController> logger)
    {
        _handler = handler;
        _configuration = configuration;
        _logger = logger;
    }
    [Authorize]
    [HttpGet("reservas/{requestId:guid}/estado")]
    public async Task<IActionResult> Index(Guid requestId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        // Polling policy agreed in #16: every 3 s for up to 2 min, configurable (at least 1 s, so a
        // bad value cannot turn the page into a busy loop).
        var model = new ReservationStatusViewModel
        {
            RequestId = requestId,
            PollIntervalSeconds = Math.Max(1, _configuration.GetValue("ReservationStatusPolling:IntervalSeconds", 3)),
            PollMaxSeconds = Math.Max(1, _configuration.GetValue("ReservationStatusPolling:MaxSeconds", 120))
        };
        try
        {
            var result = await _handler.HandleAsync(requestId, userId, cancellationToken);
            model.Status = result?.Status;
            model.RejectionReason = result?.RejectionReason;
            return View(model);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (BookingUnavailableException exception)
        {
            _logger.LogError(
                exception.InnerException,
                "Booking could not report the status of reservation request {RequestId}.",
                exception.RequestId);
            // A Booking failure is not a missing or rejected request: the view says so.
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            model.BookingUnavailable = true;
            return View(model);
        }
    }
}