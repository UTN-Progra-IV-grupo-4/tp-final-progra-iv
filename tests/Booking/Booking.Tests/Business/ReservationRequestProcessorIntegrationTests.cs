using Booking.Business.Reservations;
using Booking.Data.Models;
using Booking.Data.Repositories;
using Booking.Tests.Data;
using Booking.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Business;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationRequestProcessorIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_WhenRequestsCompete_StoresRetainedAndRejectedSeatTaken()
    {
        await fixture.ResetAsync();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var firstRequest = new ProcessReservationRequest(Guid.NewGuid(), eventId, seatId);
        var secondRequest = new ProcessReservationRequest(Guid.NewGuid(), eventId, seatId);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = ProcessAsync(firstRequest, startGate.Task);
        var secondTask = ProcessAsync(secondRequest, startGate.Task);
        startGate.SetResult();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result.Outcome == ReservationProcessingOutcome.Retained);
        Assert.Single(results, result => result.Outcome == ReservationProcessingOutcome.SeatTaken);

        await using var verificationDbContext = fixture.CreateDbContext();
        var reservations = await verificationDbContext.Reservations
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, reservations.Count);

        var retainedReservation = Assert.Single(
            reservations,
            reservation => reservation.Status == ReservationStatus.Retained);
        Assert.Equal(UtcNow.AddMinutes(10), retainedReservation.ExpiresAt);
        Assert.Null(retainedReservation.RejectionReason);

        var rejectedReservation = Assert.Single(
            reservations,
            reservation => reservation.Status == ReservationStatus.Rejected);
        Assert.Equal(ReservationRejectionReason.SeatTaken, rejectedReservation.RejectionReason);
        Assert.Null(rejectedReservation.ExpiresAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenManyRequestsCompeteForSameSeat_StoresOneRetainedAndOtherRejections()
    {
        await fixture.ResetAsync();
        const int requestCount = 10;
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var requests = Enumerable.Range(0, requestCount)
            .Select(_ => new ProcessReservationRequest(Guid.NewGuid(), eventId, seatId))
            .ToArray();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = requests.Select(request => ProcessAsync(request, startGate.Task)).ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, result => result.Outcome == ReservationProcessingOutcome.Retained);
        Assert.Equal(requestCount - 1, results.Count(result =>
            result.Outcome == ReservationProcessingOutcome.SeatTaken));

        await using var verificationDbContext = fixture.CreateDbContext();
        var reservations = await verificationDbContext.Reservations.AsNoTracking().ToListAsync();
        Assert.Equal(requestCount, reservations.Count);
        Assert.Equal(requestCount, reservations.Select(reservation => reservation.Id).Distinct().Count());
        Assert.All(reservations, reservation =>
        {
            Assert.Equal(eventId, reservation.EventId);
            Assert.Equal(seatId, reservation.SeatId);
        });
        Assert.Single(reservations, reservation => reservation.Status == ReservationStatus.Retained);
        Assert.Equal(requestCount - 1, reservations.Count(reservation =>
            reservation.Status == ReservationStatus.Rejected &&
            reservation.RejectionReason == ReservationRejectionReason.SeatTaken));
    }

    [Fact]
    public async Task ProcessAsync_WhenSameRequestIdIsDeliveredConcurrently_StoresOneReservationAndReturnsDuplicate()
    {
        await fixture.ResetAsync();
        var request = new ProcessReservationRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = ProcessAsync(request, startGate.Task);
        var secondTask = ProcessAsync(request, startGate.Task);

        startGate.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result.Outcome == ReservationProcessingOutcome.Retained);
        Assert.Single(results, result => result.Outcome == ReservationProcessingOutcome.Duplicate);

        await using var verificationDbContext = fixture.CreateDbContext();
        var reservation = await verificationDbContext.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(request.RequestId, reservation.Id);
        Assert.Equal(request.EventId, reservation.EventId);
        Assert.Equal(request.SeatId, reservation.SeatId);
        Assert.Equal(ReservationStatus.Retained, reservation.Status);
    }

    private async Task<ReservationProcessingResult> ProcessAsync(
        ProcessReservationRequest request,
        Task startGate)
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ReservationRepository(dbContext);
        var processor = new ReservationRequestProcessor(
            repository,
            Options.Create(new ReservationProcessingOptions
            {
                RetentionDuration = TimeSpan.FromMinutes(10)
            }),
            new FixedTimeProvider(UtcNow));

        await startGate;
        return await processor.ProcessAsync(request);
    }
}
