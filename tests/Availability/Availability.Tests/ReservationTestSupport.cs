using Availability.Application.Abstractions;
using Availability.Domain.Entities;

namespace Availability.Tests;

public sealed class RecordingMessagePublisher : IMessagePublisher
{
    public List<object> PublishedMessages { get; } = [];

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        PublishedMessages.Add(message!);
        return Task.CompletedTask;
    }
}

public sealed class ThrowingMessagePublisher(Exception exception) : IMessagePublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        => Task.FromException(exception);
}

public static class ReservationSeed
{
    public static async Task<(Guid EventId, Guid SeatId)> SeedEventWithSeatAsync(
        PostgreSqlFixture fixture,
        string eventName = "Test event",
        string seatNumber = "A1")
    {
        var seat = new Seat { Id = Guid.NewGuid(), Number = seatNumber };
        var seatEvent = new Event
        {
            Id = Guid.NewGuid(),
            Name = eventName,
            StartsAt = DateTimeOffset.UtcNow.AddDays(30),
            Seats = { seat }
        };
        await using var context = fixture.CreateDbContext();
        context.Events.Add(seatEvent);
        await context.SaveChangesAsync();
        return (seatEvent.Id, seat.Id);
    }
}
