using Availability.Application.Abstractions;
using Availability.Application.Logging;
using Availability.Application.ReservationRequests;
using Availability.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;
namespace Availability.Tests;
[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationStatusHandlerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task HandleAsync_OwnRequest_ReturnsTheStatusFromBooking()
    {
        var requestId = await SeedRequestAsync("user-42");
        var booking = new StubBookingStatusClient(id => new ReservationStatusResult
        {
            RequestId = id,
            Status = ReservationStatus.Confirmed
        });
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationStatusHandler(new ReservationRequestRepository(context), booking);
        var result = await handler.HandleAsync(requestId, "user-42");
        Assert.NotNull(result);
        Assert.Equal(ReservationStatus.Confirmed, result.Status);
        Assert.Equal(requestId, Assert.Single(booking.RequestedIds));
    }
    [Fact]
    public async Task HandleAsync_BookingHasNotConsumedTheRequest_ReturnsNullAsProcessing()
    {
        var requestId = await SeedRequestAsync("user-42");
        var booking = new StubBookingStatusClient(_ => null);
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationStatusHandler(new ReservationRequestRepository(context), booking);
        var result = await handler.HandleAsync(requestId, "user-42");
        Assert.Null(result);
    }
    [Fact]
    public async Task HandleAsync_RequestOfAnotherUser_ThrowsNotFoundAndDoesNotAskBooking()
    {
        var requestId = await SeedRequestAsync("user-42");
        var booking = new StubBookingStatusClient(_ => null);
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationStatusHandler(new ReservationRequestRepository(context), booking);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(requestId, "user-99"));
        Assert.Empty(booking.RequestedIds);
    }
    [Fact]
    public async Task HandleAsync_UnknownRequest_ThrowsNotFoundAndDoesNotAskBooking()
    {
        await fixture.ResetAsync();
        var booking = new StubBookingStatusClient(_ => null);
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationStatusHandler(new ReservationRequestRepository(context), booking);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(Guid.NewGuid(), "user-42"));
        Assert.Empty(booking.RequestedIds);
    }
    [Fact]
    public async Task HandleAsync_BookingUnavailable_ThrowsBookingUnavailableException()
    {
        var requestId = await SeedRequestAsync("user-42");
        var booking = new StubBookingStatusClient(id => throw new BookingUnavailableException(id));
        await using var context = fixture.CreateDbContext();
        var handler = new ReservationStatusHandler(new ReservationRequestRepository(context), booking);
        var exception = await Assert.ThrowsAsync<BookingUnavailableException>(
            () => handler.HandleAsync(requestId, "user-42"));
        Assert.Equal(requestId, exception.RequestId);
    }
    [Fact]
    public async Task HandleAsync_BookingStillDoesNotKnowTheRequest_WarnsOnlyInsideTheWindowAfterTheThreshold()
    {
        var requestId = await SeedRequestAsync("user-42");
        var logger = new RecordingLogger<ReservationStatusHandler>();
        await using var context = fixture.CreateDbContext();
        var requestedAt = (await new ReservationRequestRepository(context).GetByIdAsync(requestId))!.RequestedAt;
        var booking = new StubBookingStatusClient(_ => null);

        // The window is [15 s, 21 s): the first edge warns, the second does not. Before it and after it the
        // handler stays quiet, so a poll every few seconds does not flood the log.
        foreach (var elapsedSeconds in new[] { 5, 14, 15, 20, 21, 40 })
        {
            var handler = new ReservationStatusHandler(
                new ReservationRequestRepository(context),
                booking,
                logger,
                new FixedTimeProvider(requestedAt.AddSeconds(elapsedSeconds)));
            Assert.Null(await handler.HandleAsync(requestId, "user-42"));
        }

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(AvailabilityAuditEvents.ReservationRequestNotRegistered, warning.EventId);
            Assert.Contains($"RequestId: {requestId}", warning.Message);
            Assert.Contains($"CorrelationId: {requestId}", warning.Message);
            Assert.Contains("Service: Availability", warning.Message);
            Assert.DoesNotContain("user-42", warning.Message);
        });
        Assert.Contains("ElapsedSeconds: 15", warnings[0].Message);
        Assert.Contains("ElapsedSeconds: 20", warnings[1].Message);
    }
    [Fact]
    public async Task HandleAsync_BookingAnswersAfterTheThreshold_DoesNotWarn()
    {
        var requestId = await SeedRequestAsync("user-42");
        var logger = new RecordingLogger<ReservationStatusHandler>();
        await using var context = fixture.CreateDbContext();
        var requestedAt = (await new ReservationRequestRepository(context).GetByIdAsync(requestId))!.RequestedAt;
        var booking = new StubBookingStatusClient(id => new ReservationStatusResult
        {
            RequestId = id,
            Status = ReservationStatus.Retained
        });
        var handler = new ReservationStatusHandler(
            new ReservationRequestRepository(context),
            booking,
            logger,
            new FixedTimeProvider(requestedAt.AddSeconds(16)));
        var result = await handler.HandleAsync(requestId, "user-42");
        Assert.NotNull(result);
        Assert.Empty(logger.Entries);
    }
    // A clock that always says the same instant, to place a poll at an exact age of the request.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    // Stores a request through the existing handler, so the row is built exactly as in production.
    private async Task<Guid> SeedRequestAsync(string userId)
    {
        await fixture.ResetAsync();
        var (eventId, seatId) = await ReservationSeed.SeedEventWithSeatAsync(fixture);
        await using var context = fixture.CreateDbContext();
        var requestHandler = new ReservationRequestHandler(
            new ReservationRequestRepository(context),
            new RecordingMessagePublisher());
        var request = await requestHandler.HandleAsync(
            new ReservationRequestCommand { EventId = eventId, SeatId = seatId }, userId);
        return request.Id;
    }
    // Answers with the given function and records which ids Booking was asked about.
    private sealed class StubBookingStatusClient(Func<Guid, ReservationStatusResult?> answer) : IBookingStatusClient
    {
        public List<Guid> RequestedIds { get; } = [];
        public Task<ReservationStatusResult?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(requestId);
            return Task.FromResult(answer(requestId));
        }
    }
}