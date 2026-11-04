using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Persistence.Repositories;

public class ReservationRequestRepository(AvailabilityDbContext dbContext) : IReservationRequestRepository
{
    public Task<bool> SeatExistsAsync(Guid seatId, Guid eventId, CancellationToken cancellationToken = default)
    {
        // The seat must belong to the event: a valid seat id of another event is not a valid request.
        return dbContext.Seats.AnyAsync(
            seat => seat.Id == seatId && seat.EventId == eventId,
            cancellationToken);
    }

    public async Task AddAsync(ReservationRequest reservationRequest, CancellationToken cancellationToken = default)
    {
        dbContext.ReservationRequests.Add(reservationRequest);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
