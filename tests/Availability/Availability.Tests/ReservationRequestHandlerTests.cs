using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Persistence.Repositories;
using Contracts;
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

    [Fact]
    public async Task HandleAsync_SeatOfAnotherEvent_ThrowsInvalidOperationAndStoresNothing()
    {
        await fixture.ResetAsync();
        var (_, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var (otherEventId, _) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var publisher = new RecordingMessagePublisher();
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(new ReservationRequestRepository(context), publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new ReservationRequestCommand { EventId = otherEventId, SeatId = seatId }, "user-42"));

        await using var verification = fixture.CreateDbContext();
        Assert.Empty(await verification.ReservationRequests.ToListAsync());
        Assert.Empty(publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_StoresTheRowAndPublishesTheMatchingMessage()
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        var publisher = new RecordingMessagePublisher();
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationRequestHandler(new ReservationRequestRepository(context), publisher);

        var result = await handler.HandleAsync(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, "user-42");

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.ReservationRequests.SingleAsync();
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal("user-42", stored.UserId);
        Assert.Equal(seatId, stored.SeatId);
        var message = Assert.IsType<ReservationRequestMessage>(Assert.Single(publisher.PublishedMessages));
        Assert.Equal(stored.Id, message.MessageId);
        Assert.Equal(stored.Id.ToString(), message.CorrelationId);
        Assert.Equal("user-42", message.UserId);
        Assert.Equal(eventId, message.EventId);
        Assert.Equal(seatId, message.SeatId);
    }
}
