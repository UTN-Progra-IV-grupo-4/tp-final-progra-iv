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

        async Task<ReservationProcessingResult> ProcessAsync(ProcessReservationRequest request)
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

            await startGate.Task;
            return await processor.ProcessAsync(request);
        }

        var firstTask = ProcessAsync(firstRequest);
        var secondTask = ProcessAsync(secondRequest);
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
}
