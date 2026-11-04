using Availability.Domain.Entities;
using Availability.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Availability.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationRequestRepositoryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task SeatExistsAsync_SeatOfTheEvent_ReturnsTrue()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await SeedEventWithSeatAsync();
        await using var context = fixture.CreateDbContext();

        var exists = await new ReservationRequestRepository(context).SeatExistsAsync(seatId, eventId);

        Assert.True(exists);
    }

    [Fact]
    public async Task SeatExistsAsync_SeatOfAnotherEvent_ReturnsFalse()
    {
        await fixture.ResetAsync();
        var (_, seatId) = await SeedEventWithSeatAsync();
        var (otherEventId, _) = await SeedEventWithSeatAsync();
        await using var context = fixture.CreateDbContext();

        var exists = await new ReservationRequestRepository(context).SeatExistsAsync(seatId, otherEventId);

        Assert.False(exists);
    }

    [Fact]
    public async Task SeatExistsAsync_UnknownSeat_ReturnsFalse()
    {
        await fixture.ResetAsync();
        var (eventId, _) = await SeedEventWithSeatAsync();
        await using var context = fixture.CreateDbContext();

        var exists = await new ReservationRequestRepository(context).SeatExistsAsync(Guid.NewGuid(), eventId);

        Assert.False(exists);
    }

    [Fact]
    public async Task AddAsync_PersistsTheRequest()
    {
        await fixture.ResetAsync();
        var (_, seatId) = await SeedEventWithSeatAsync();
        var reservationRequest = new ReservationRequest
        {
            Id = Guid.NewGuid(),
            SeatId = seatId,
            UserId = "user-42",
            RequestedAt = DateTimeOffset.UtcNow
        };
        await using (var context = fixture.CreateDbContext())
        {
            await new ReservationRequestRepository(context).AddAsync(reservationRequest);
        }

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.ReservationRequests.SingleAsync();
        Assert.Equal(reservationRequest.Id, stored.Id);
        Assert.Equal(seatId, stored.SeatId);
        Assert.Equal("user-42", stored.UserId);
    }

    private async Task<(Guid EventId, Guid SeatId)> SeedEventWithSeatAsync()
    {
        var seat = new Seat { Id = Guid.NewGuid(), Number = "A1" };
        var seatEvent = new Event
        {
            Id = Guid.NewGuid(),
            Name = "Test event",
            StartsAt = DateTimeOffset.UtcNow.AddDays(30),
            Seats = { seat }
        };
        await using var context = fixture.CreateDbContext();
        context.Events.Add(seatEvent);
        await context.SaveChangesAsync();
        return (seatEvent.Id, seat.Id);
    }
}
