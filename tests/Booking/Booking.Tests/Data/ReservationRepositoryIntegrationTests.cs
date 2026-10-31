using Booking.Data.Models;
using Booking.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Booking.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationRepositoryIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task TryInsertAsync_WhenSeatIsFree_CreatesRetainedReservation()
    {
        await fixture.ResetAsync();
        var reservation = CreateRetainedReservation();

        await using var dbContext = fixture.CreateDbContext();
        var repository = new ReservationRepository(dbContext);

        var result = await repository.TryInsertAsync(reservation);

        Assert.Equal(ReservationInsertOutcome.Created, result.Outcome);

        var storedReservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(reservation.Id, storedReservation.Id);
        Assert.Equal(ReservationStatus.Retained, storedReservation.Status);
    }

    [Fact]
    public async Task TryInsertAsync_WhenRequestIdExists_ReturnsExistingReservation()
    {
        await fixture.ResetAsync();
        var reservation = CreateRetainedReservation();

        await using (var firstDbContext = fixture.CreateDbContext())
        {
            var firstRepository = new ReservationRepository(firstDbContext);
            var firstResult = await firstRepository.TryInsertAsync(reservation);
            Assert.Equal(ReservationInsertOutcome.Created, firstResult.Outcome);
        }

        var duplicate = CreateRetainedReservation(
            reservation.Id,
            reservation.EventId,
            reservation.SeatId);

        await using var duplicateDbContext = fixture.CreateDbContext();
        var duplicateRepository = new ReservationRepository(duplicateDbContext);

        var duplicateResult = await duplicateRepository.TryInsertAsync(duplicate);

        Assert.Equal(ReservationInsertOutcome.RequestIdExists, duplicateResult.Outcome);
        Assert.NotNull(duplicateResult.ExistingReservation);
        Assert.Equal(reservation.Id, duplicateResult.ExistingReservation.Id);
        Assert.Equal(reservation.EventId, duplicateResult.ExistingReservation.EventId);
        Assert.Equal(reservation.SeatId, duplicateResult.ExistingReservation.SeatId);
    }

    [Fact]
    public async Task TryInsertAsync_WhenRequestsCompeteForSameSeat_ReturnsCreatedAndSeatTaken()
    {
        await fixture.ResetAsync();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var firstReservation = CreateRetainedReservation(Guid.NewGuid(), eventId, seatId);
        var secondReservation = CreateRetainedReservation(Guid.NewGuid(), eventId, seatId);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ReservationInsertResult> InsertAsync(Reservation reservation)
        {
            await using var dbContext = fixture.CreateDbContext();
            var repository = new ReservationRepository(dbContext);

            await startGate.Task;
            return await repository.TryInsertAsync(reservation);
        }

        var firstTask = InsertAsync(firstReservation);
        var secondTask = InsertAsync(secondReservation);
        startGate.SetResult();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result.Outcome == ReservationInsertOutcome.Created);
        Assert.Single(results, result => result.Outcome == ReservationInsertOutcome.SeatTaken);

        await using var verificationDbContext = fixture.CreateDbContext();
        var storedReservation = await verificationDbContext.Reservations
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(ReservationStatus.Retained, storedReservation.Status);
        Assert.Equal(eventId, storedReservation.EventId);
        Assert.Equal(seatId, storedReservation.SeatId);
    }

    [Fact]
    public async Task TryInsertAsync_AfterSeatConflict_AllowsRejectedInsertWithSameContext()
    {
        await fixture.ResetAsync();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var retainedReservation = CreateRetainedReservation(Guid.NewGuid(), eventId, seatId);

        await using (var retainedDbContext = fixture.CreateDbContext())
        {
            var retainedRepository = new ReservationRepository(retainedDbContext);
            var retainedResult = await retainedRepository.TryInsertAsync(retainedReservation);
            Assert.Equal(ReservationInsertOutcome.Created, retainedResult.Outcome);
        }

        var losingReservation = CreateRetainedReservation(Guid.NewGuid(), eventId, seatId);

        await using var losingDbContext = fixture.CreateDbContext();
        var losingRepository = new ReservationRepository(losingDbContext);

        var seatConflictResult = await losingRepository.TryInsertAsync(losingReservation);
        Assert.Equal(ReservationInsertOutcome.SeatTaken, seatConflictResult.Outcome);

        losingReservation.Status = ReservationStatus.Rejected;
        losingReservation.RejectionReason = ReservationRejectionReason.SeatTaken;
        losingReservation.ExpiresAt = null;

        var rejectedResult = await losingRepository.TryInsertAsync(losingReservation);
        Assert.Equal(ReservationInsertOutcome.Created, rejectedResult.Outcome);

        var storedReservations = await losingDbContext.Reservations
            .AsNoTracking()
            .OrderBy(reservation => reservation.Status)
            .ToListAsync();

        Assert.Equal(2, storedReservations.Count);
        Assert.Contains(storedReservations, reservation =>
            reservation.Status == ReservationStatus.Retained &&
            reservation.RejectionReason is null);
        Assert.Contains(storedReservations, reservation =>
            reservation.Status == ReservationStatus.Rejected &&
            reservation.RejectionReason == ReservationRejectionReason.SeatTaken);
    }

    private static Reservation CreateRetainedReservation(
        Guid? requestId = null,
        Guid? eventId = null,
        Guid? seatId = null) =>
        new()
        {
            Id = requestId ?? Guid.NewGuid(),
            EventId = eventId ?? Guid.NewGuid(),
            SeatId = seatId ?? Guid.NewGuid(),
            Status = ReservationStatus.Retained,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
}
