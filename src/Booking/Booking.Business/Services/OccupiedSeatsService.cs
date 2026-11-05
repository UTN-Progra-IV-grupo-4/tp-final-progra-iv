using Booking.Business.Dtos;
using Booking.Data.Repositories;

namespace Booking.Business.Services;

// Coordina la consulta de ocupación y construye su respuesta.
public class OccupiedSeatsService
{
    private readonly IReservationRepository _reservationRepository;
    private readonly TimeProvider _timeProvider;

    public OccupiedSeatsService(
        IReservationRepository reservationRepository,
        TimeProvider timeProvider)
    {
        _reservationRepository = reservationRepository;
        _timeProvider = timeProvider;
    }

    public async Task<OccupiedSeatsResponse> GetOccupiedSeatsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        // Evalúa todas las retenciones contra el mismo instante UTC.
        var now = _timeProvider.GetUtcNow();

        var seatIds = await _reservationRepository.GetOccupiedSeatIdsAsync(
            eventId,
            now,
            cancellationToken);

        // Una consulta sin coincidencias conserva la lista vacía.
        return new OccupiedSeatsResponse
        {
            EventId = eventId,
            SeatIds = seatIds
        };
    }
}
