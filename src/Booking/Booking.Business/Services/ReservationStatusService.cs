using Booking.Business.Dtos;
using Booking.Data.Models;
using Booking.Data.Repositories;

namespace Booking.Business.Services;

// Coordina la consulta del estado y su transformación al DTO.
public class ReservationStatusService
{
    private readonly ReservationRepository _reservationRepository;

    // Recibe el repositorio mediante inyección de dependencias.
    public ReservationStatusService(
        ReservationRepository reservationRepository)
    {
        _reservationRepository = reservationRepository;
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

        // Expone únicamente los datos definidos en el contrato.
        return new ReservationStatusResponse
        {
            RequestId = reservation.Id,
            Status = reservation.Status.ToString(),
            RejectionReason =
                reservation.Status == ReservationStatus.Rejected
                    ? reservation.RejectionReason?.ToString()
                    : null
        };
    }
}
