using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Availability.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationRequestHandlerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task HandleAsync_PublisherThrows_RemovesTheStoredRequestAndThrowsPublishException()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var brokerFailure = new InvalidOperationException("broker down");
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(
            new ReservationRequestRepository(context),
            new ThrowingMessagePublisher(brokerFailure));

        var exception = await Assert.ThrowsAsync<ReservationRequestPublishException>(
            () => handler.HandleAsync(new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, "user-42"));

        Assert.Same(brokerFailure, exception.InnerException);
        await using var verification = fixture.CreateDbContext();
        Assert.Empty(await verification.ReservationRequests.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_PublishCancelled_RemovesTheStoredRequestAndRethrowsTheCancellation()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(
            new ReservationRequestRepository(context),
            new ThrowingMessagePublisher(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, "user-42"));

        await using var verification = fixture.CreateDbContext();
        Assert.Empty(await verification.ReservationRequests.ToListAsync());
    }
}
