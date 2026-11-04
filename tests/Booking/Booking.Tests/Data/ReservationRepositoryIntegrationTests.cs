using Booking.Business.Reservations;
using Booking.Data.Models;
using Booking.Data.Repositories;
using Booking.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReservationRepositoryIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task GetOccupiedSeatIdsAsync_ReturnsOnlyOccupiedSeatsForRequestedEvent()
    {
        await fixture.ResetAsync();
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var eventId = Guid.NewGuid();

        var confirmed = CreateReservationForEvent(
            eventId, ReservationStatus.Confirmed, now.AddMinutes(-5));
        var retainedFuture = CreateReservationForEvent(
            eventId, ReservationStatus.Retained, now.AddMilliseconds(1));
        var retainedWithoutExpiry = CreateReservationForEvent(
            eventId, ReservationStatus.Retained, null);
        var retainedAtLimit = CreateReservationForEvent(
            eventId, ReservationStatus.Retained, now);
        var retainedOverdue = CreateReservationForEvent(
            eventId, ReservationStatus.Retained, now.AddMilliseconds(-1));
        var rejected = CreateReservationForEvent(
            eventId, ReservationStatus.Rejected, null);
        rejected.RejectionReason = ReservationRejectionReason.PaymentFailed;
        var expired = CreateReservationForEvent(
            eventId, ReservationStatus.Expired, now.AddMinutes(-5));
        var confirmedInOtherEvent = CreateReservationForEvent(
            Guid.NewGuid(), ReservationStatus.Confirmed, null);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.AddRange(
            confirmed, retainedFuture, retainedWithoutExpiry, retainedAtLimit,
            retainedOverdue, rejected, expired, confirmedInOtherEvent);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);
        var actual = await repository.GetOccupiedSeatIdsAsync(eventId, now);

        var expected = new[]
        {
            confirmed.SeatId,
            retainedFuture.SeatId,
            retainedWithoutExpiry.SeatId
        };

        // El contrato exige los IDs exactos, sin imponer un orden.
        Assert.Equal(expected.Order(), actual.Order());
    }

    [Fact]
    public async Task GetOccupiedSeatIdsAsync_WhenEventHasNoReservations_ReturnsEmptyList()
    {
        await fixture.ResetAsync();
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var eventWithoutReservations = Guid.NewGuid();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.Add(CreateReservationForEvent(
            Guid.NewGuid(), ReservationStatus.Confirmed, null));
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);
        var actual = await repository.GetOccupiedSeatIdsAsync(eventWithoutReservations, now);

        Assert.Empty(actual);
    }

    [Fact]
    public async Task GetExpiredReservationsAsync_ReturnsOnlyRetainedReservationsExpiredAtOrBeforeNow()
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var expiredReservation = CreateRetainedReservation();
        expiredReservation.ExpiresAt = now;
        var futureReservation = CreateRetainedReservation();
        futureReservation.ExpiresAt = now.AddMilliseconds(1);
        var confirmedReservation = CreateRetainedReservation();
        confirmedReservation.Status = ReservationStatus.Confirmed;
        confirmedReservation.ExpiresAt = now.AddMinutes(-1);
        var noExpiryReservation = CreateRetainedReservation();
        noExpiryReservation.ExpiresAt = null;

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.AddRange(
            expiredReservation,
            futureReservation,
            confirmedReservation,
            noExpiryReservation);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);

        var expiredReservations = await repository.GetExpiredReservationsAsync(now, 10);

        var reservation = Assert.Single(expiredReservations);
        Assert.Equal(expiredReservation.Id, reservation.Id);
    }

    [Fact]
    public async Task GetExpiredReservationsAsync_ReturnsEarliestReservationsWithinBatchSize()
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var reservations = new[]
        {
            CreateRetainedReservation(),
            CreateRetainedReservation(),
            CreateRetainedReservation()
        };
        reservations[0].ExpiresAt = now.AddMinutes(-1);
        reservations[1].ExpiresAt = now.AddMinutes(-3);
        reservations[2].ExpiresAt = now.AddMinutes(-2);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.AddRange(reservations);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);

        var expiredReservations = await repository.GetExpiredReservationsAsync(now, 2);

        Assert.Equal(2, expiredReservations.Count);
        Assert.Equal(
            new[] { reservations[1].Id, reservations[2].Id },
            expiredReservations.Select(reservation => reservation.Id));
    }

    [Fact]
    public async Task TryExpireAsync_WhenReservationIsStillExpiredAndRetained_UpdatesItToExpired()
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var reservation = CreateRetainedReservation();
        reservation.ExpiresAt = now;

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);

        var expired = await repository.TryExpireAsync(reservation.Id, now);

        Assert.True(expired);
        var storedReservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(ReservationStatus.Expired, storedReservation.Status);
    }

    [Fact]
    public async Task TryExpireRetainedReservationForSeatAsync_WhenMatchingRetainedReservationIsExpired_ExpiresIt()
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var reservation = CreateRetainedReservation();
        reservation.ExpiresAt = now;

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);

        var expired = await repository.TryExpireRetainedReservationForSeatAsync(
            reservation.EventId,
            reservation.SeatId,
            now);

        Assert.True(expired);
        var storedReservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(ReservationStatus.Expired, storedReservation.Status);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed, -1)]
    [InlineData(ReservationStatus.Retained, 1)]
    public async Task TryExpireRetainedReservationForSeatAsync_WhenReservationIsNotExpiredRetained_DoesNotExpire(
        ReservationStatus status,
        int expiryOffsetSeconds)
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var reservation = CreateRetainedReservation();
        reservation.Status = status;
        reservation.ExpiresAt = now.AddSeconds(expiryOffsetSeconds);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);

        var expired = await repository.TryExpireRetainedReservationForSeatAsync(
            reservation.EventId,
            reservation.SeatId,
            now);

        Assert.False(expired);
        var storedReservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(status, storedReservation.Status);
    }

    [Fact]
    public async Task ExpireRetainedReservations_WhenSeatIsReleased_AllowsNewReservationForSameEventAndSeat()
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var expiredReservation = CreateRetainedReservation(
            Guid.NewGuid(),
            eventId,
            seatId);
        expiredReservation.ExpiresAt = now.AddTicks(-1);

        await using (var initialDbContext = fixture.CreateDbContext())
        {
            var initialRepository = new ReservationRepository(initialDbContext);
            var insertResult = await initialRepository.TryInsertAsync(expiredReservation);
            Assert.Equal(ReservationInsertOutcome.Created, insertResult.Outcome);
        }

        await using (var expirationDbContext = fixture.CreateDbContext())
        {
            var expirationRepository = new ReservationRepository(expirationDbContext);
            var useCase = new ExpireRetainedReservations(
                expirationRepository,
                new FixedTimeProvider(now),
                Options.Create(new ReservationProcessingOptions
                {
                    ExpirationBatchSize = 100
                }));

            var result = await useCase.ExecuteAsync();

            Assert.Equal(1, result.ProcessedCount);
            Assert.Equal(new[] { expiredReservation.Id }, result.ReservationIds);
            Assert.Equal(now, result.ExecutedAtUtc);
        }

        var newReservation = CreateRetainedReservation(
            Guid.NewGuid(),
            eventId,
            seatId);
        await using (var newReservationDbContext = fixture.CreateDbContext())
        {
            var newReservationRepository = new ReservationRepository(newReservationDbContext);

            var insertResult = await newReservationRepository.TryInsertAsync(newReservation);

            Assert.Equal(ReservationInsertOutcome.Created, insertResult.Outcome);
        }

        await using var verificationDbContext = fixture.CreateDbContext();
        var storedReservations = await verificationDbContext.Reservations
            .AsNoTracking()
            .OrderBy(reservation => reservation.Status)
            .ToListAsync();

        Assert.Equal(2, storedReservations.Count);
        Assert.Contains(storedReservations, reservation =>
            reservation.Id == expiredReservation.Id &&
            reservation.EventId == eventId &&
            reservation.SeatId == seatId &&
            reservation.Status == ReservationStatus.Expired);
        Assert.Contains(storedReservations, reservation =>
            reservation.Id == newReservation.Id &&
            reservation.EventId == eventId &&
            reservation.SeatId == seatId &&
            reservation.Status == ReservationStatus.Retained);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed, -1)]
    [InlineData(ReservationStatus.Retained, 1)]
    public async Task TryExpireAsync_WhenReservationNoLongerMatchesConditions_DoesNotUpdate(
        ReservationStatus status,
        int expiryOffsetMinutes)
    {
        await fixture.ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var reservation = CreateRetainedReservation();
        reservation.Status = status;
        reservation.ExpiresAt = now.AddMinutes(expiryOffsetMinutes);

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync();

        var repository = new ReservationRepository(dbContext);

        var expired = await repository.TryExpireAsync(reservation.Id, now);

        Assert.False(expired);
        var storedReservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(status, storedReservation.Status);
    }

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
        Assert.Equal(
            EntityState.Detached,
            losingDbContext.Entry(losingReservation).State);

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

    [Fact]
    public async Task TryInsertAsync_WhenPartialUniqueIndexRaises23505_ReturnsSeatTakenAndDetachesAttempt()
    {
        await fixture.ResetAsync();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var activeReservation = CreateRetainedReservation(Guid.NewGuid(), eventId, seatId);
        var conflictingReservation = CreateRetainedReservation(Guid.NewGuid(), eventId, seatId);

        await using var dbContext = fixture.CreateDbContext();
        var repository = new ReservationRepository(dbContext);
        Assert.Equal(
            ReservationInsertOutcome.Created,
            (await repository.TryInsertAsync(activeReservation)).Outcome);

        var conflictResult = await repository.TryInsertAsync(conflictingReservation);

        Assert.Equal(ReservationInsertOutcome.SeatTaken, conflictResult.Outcome);
        Assert.Equal(EntityState.Detached, dbContext.Entry(conflictingReservation).State);

        var unrelatedReservation = CreateRetainedReservation();
        var subsequentInsert = await repository.TryInsertAsync(unrelatedReservation);

        Assert.Equal(ReservationInsertOutcome.Created, subsequentInsert.Outcome);
        Assert.Equal(EntityState.Detached, dbContext.Entry(conflictingReservation).State);
    }

    private static Reservation CreateReservationForEvent(
        Guid eventId,
        ReservationStatus status,
        DateTimeOffset? expiresAt)
    {
        var reservation = CreateRetainedReservation(eventId: eventId);
        reservation.Status = status;
        reservation.ExpiresAt = expiresAt;
        return reservation;
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
