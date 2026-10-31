using Booking.Business.Reservations;
using Booking.Data.Models;
using Booking.Tests.TestDoubles;

namespace Booking.Tests.Business;

public sealed class ExpireRetainedReservationsTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ExpiresCandidatesAndReturnsSuccessfullyProcessedIdsAndUtcTimestamp()
    {
        var repository = new StubReservationRepository();
        var expiredReservation = CreateExpiredReservation();
        var reservationThatChangedBeforeUpdate = CreateExpiredReservation();
        repository.AddExpiredReservation(expiredReservation);
        repository.AddExpiredReservation(reservationThatChangedBeforeUpdate, expirationSucceeds: false);
        var useCase = new ExpireRetainedReservations(
            repository,
            new FixedTimeProvider(UtcNow));

        var result = await useCase.ExecuteAsync();

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(new[] { expiredReservation.Id }, result.ReservationIds);
        Assert.Equal(UtcNow, result.ExecutedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.ExecutedAtUtc.Offset);
        Assert.Equal(
            new[] { expiredReservation.Id, reservationThatChangedBeforeUpdate.Id },
            repository.ExpirationAttempts.Select(attempt => attempt.ReservationId));
        Assert.All(repository.ExpirationAttempts, attempt => Assert.Equal(UtcNow, attempt.Now));
    }

    [Fact]
    public async Task ExecuteAsync_WhenThereAreNoExpiredReservations_ReturnsEmptyResult()
    {
        var repository = new StubReservationRepository();
        var useCase = new ExpireRetainedReservations(
            repository,
            new FixedTimeProvider(UtcNow));

        var result = await useCase.ExecuteAsync();

        Assert.Equal(0, result.ProcessedCount);
        Assert.Empty(result.ReservationIds);
        Assert.Equal(UtcNow, result.ExecutedAtUtc);
        Assert.Empty(repository.ExpirationAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesOnlyExpiredRetainedReservations()
    {
        var repository = new StubReservationRepository();
        var retainedNotExpired = CreateReservation(ReservationStatus.Retained, UtcNow.AddTicks(1));
        var retainedExpired = CreateReservation(ReservationStatus.Retained, UtcNow);
        var confirmed = CreateReservation(ReservationStatus.Confirmed, UtcNow.AddMinutes(-1));
        var rejected = CreateReservation(ReservationStatus.Rejected, null);
        repository.AddExpiredReservation(retainedNotExpired);
        repository.AddExpiredReservation(retainedExpired);
        repository.AddExpiredReservation(confirmed);
        repository.AddExpiredReservation(rejected);
        var useCase = new ExpireRetainedReservations(
            repository,
            new FixedTimeProvider(UtcNow));

        var result = await useCase.ExecuteAsync();

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(new[] { retainedExpired.Id }, result.ReservationIds);
        var expirationAttempt = Assert.Single(repository.ExpirationAttempts);
        Assert.Equal(retainedExpired.Id, expirationAttempt.ReservationId);
        Assert.Equal(UtcNow, expirationAttempt.Now);
    }

    private static Reservation CreateExpiredReservation() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            Status = ReservationStatus.Retained,
            ExpiresAt = UtcNow
        };

    private static Reservation CreateReservation(
        ReservationStatus status,
        DateTimeOffset? expiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            Status = status,
            RejectionReason = status == ReservationStatus.Rejected
                ? ReservationRejectionReason.SeatTaken
                : null,
            ExpiresAt = expiresAt
        };
}
