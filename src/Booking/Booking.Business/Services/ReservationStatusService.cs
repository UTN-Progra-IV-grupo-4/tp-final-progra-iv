using Booking.Business.Dtos;
using Booking.Data.Models;
using Booking.Data.Repositories;

namespace Booking.Business.Services;

// Coordina la consulta del estado y su transformación al DTO.
public class ReservationStatusService
{
    private readonly IReservationRepository _reservationRepository;
    private readonly TimeProvider _timeProvider;

    // Recibe el repositorio y el reloj mediante inyección de dependencias.
    public ReservationStatusService(
        IReservationRepository reservationRepository,
        TimeProvider timeProvider)
    {
        _reservationRepository = reservationRepository;
        _timeProvider = timeProvider;
    }

    public async Task<ReservationStatusResponse?> GetStatusAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(
            requestId,
            cancellationToken);

        // Permite que la API identifique una reserva inexistente.
        if (reservation is null)
        {
            return null;
        }

        // Un rechazo debe tener un motivo registrado.
        if (reservation.Status == ReservationStatus.Rejected &&
            reservation.RejectionReason is null)
        {
            throw new InvalidOperationException(
                "A rejected reservation must have a rejection reason.");
        }

        var responseStatus = reservation.Status;
        var now = _timeProvider.GetUtcNow();

        // Refleja el vencimiento en la respuesta sin modificar la reserva.
        if (responseStatus == ReservationStatus.Retained &&
            reservation.ExpiresAt.HasValue &&
            reservation.ExpiresAt.Value <= now)
        {
            responseStatus = ReservationStatus.Expired;
        }

        // Expone únicamente los datos definidos en el contrato.
        return new ReservationStatusResponse
        {
            RequestId = reservation.Id,
            Status = responseStatus.ToString(),
            RejectionReason =
                responseStatus == ReservationStatus.Rejected
                    ? reservation.RejectionReason?.ToString()
                    : null
        };
    }
}
