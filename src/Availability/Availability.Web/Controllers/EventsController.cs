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
    public async Task<IActionResult> Seats(Guid eventId, Guid? selectedSeatId, CancellationToken cancellationToken)
    {
        var eventData = await queries.GetSeatsAsync(eventId, cancellationToken);
        if (eventData is null)
        {
            return NotFound();
        }

        if (selectedSeatId.HasValue)
        {
            var seat = eventData.Seats.SingleOrDefault(s => s.Id == selectedSeatId.Value);
            // Los radios ayudan en el navegador; la validación del servidor evita
            // seleccionar un asiento ajeno, ocupado o con disponibilidad desconocida.
            if (seat is null || seat.Availability != SeatAvailability.Available)
            {
                ModelState.AddModelError("", "El asiento elegido no está disponible para seleccionar. Actualizá el mapa.");
                selectedSeatId = null;
            }
        }

        // GET solo confirma una elección en pantalla. El envío de la reserva pertenece al #15.
        return View(new EventSeatsViewModel(eventData, displayOptions.IsDemo, selectedSeatId));
    }
}
