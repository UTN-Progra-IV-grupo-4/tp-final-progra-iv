using Availability.Application.Events;
using Availability.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Web.Controllers;

[AllowAnonymous]
[Route("eventos")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class EventsController(EventQueryService queries, EventDisplayOptions displayOptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var events = await queries.GetEventsAsync(cancellationToken);
        return View(new EventsPageViewModel(events, displayOptions.IsDemo));
    }

    [HttpGet("{eventId:guid}/asientos")]
    public async Task<IActionResult> Seats(Guid eventId, string? selectedSeatId, CancellationToken cancellationToken)
    {
        var eventData = await queries.GetSeatsAsync(eventId, cancellationToken);
        if (eventData is null)
        {
            return NotFound();
        }

        Guid? confirmedSeatId = null;
        if (!string.IsNullOrWhiteSpace(selectedSeatId))
        {
            // Se parsea a mano para no exponer el mensaje en inglés del model binding.
            var seat = Guid.TryParse(selectedSeatId, out var parsedSeatId)
                ? eventData.Seats.SingleOrDefault(candidate => candidate.Id == parsedSeatId)
                : null;
            // Los radios ayudan en el navegador; la validación del servidor evita
            // seleccionar un asiento ajeno, ocupado o con disponibilidad desconocida.
            if (seat is null || seat.Availability != SeatAvailability.Available)
            {
                ModelState.AddModelError("", "El asiento elegido no está disponible para seleccionar. Actualizá el mapa.");
            }
            else
            {
                confirmedSeatId = seat.Id;
            }
        }

        // El envío de la solicitud es un POST aparte (ReservationRequestsController).
        // GET solo confirma una elección en pantalla. El envío de la reserva pertenece al #15.
        return View(new EventSeatsViewModel(eventData, displayOptions.IsDemo, confirmedSeatId));
    }
}
