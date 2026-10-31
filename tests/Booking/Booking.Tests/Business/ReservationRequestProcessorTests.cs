using Booking.Business.Reservations;
using Booking.Data.Models;
using Booking.Data.Repositories;
using Booking.Tests.TestDoubles;
using Microsoft.Extensions.Options;

namespace Booking.Tests.Business;

public sealed class ReservationRequestProcessorTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan RetentionDuration = TimeSpan.FromMinutes(10);

    [Theory]
    [InlineData("RequestId", ReservationProcessingErrorCodes.RequestIdRequired)]
    [InlineData("EventId", ReservationProcessingErrorCodes.EventIdRequired)]
    [InlineData("SeatId", ReservationProcessingErrorCodes.SeatIdRequired)]
    public async Task ProcessAsync_WhenIdentifierIsEmpty_ReturnsInvalidWithoutPersisting(
        string emptyField,
        string expectedErrorCode)
    {
        var repository = new StubReservationRepository();
        var processor = CreateProcessor(repository);
        var request = CreateRequest();
        request = emptyField switch
        {
            "RequestId" => request with { RequestId = Guid.Empty },
            "EventId" => request with { EventId = Guid.Empty },
            "SeatId" => request with { SeatId = Guid.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(emptyField))
        };

        var result = await processor.ProcessAsync(request);

        Assert.Equal(ReservationProcessingOutcome.InvalidRequest, result.Outcome);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Empty(repository.AttemptedReservations);
    }

    [Fact]
    public async Task ProcessAsync_WhenInsertIsCreated_ReturnsRetainedWithExpiration()
    {
        var repository = new StubReservationRepository();
        repository.EnqueueResult(ReservationInsertResult.Created());
        var processor = CreateProcessor(repository);
        var request = CreateRequest();

        var result = await processor.ProcessAsync(request);

        Assert.Equal(ReservationProcessingOutcome.Retained, result.Outcome);
        Assert.Equal(ReservationStatus.Retained, result.CurrentStatus);

        var attemptedReservation = Assert.Single(repository.AttemptedReservations);
        Assert.Equal(request.RequestId, attemptedReservation.Id);
        Assert.Equal(request.EventId, attemptedReservation.EventId);
        Assert.Equal(request.SeatId, attemptedReservation.SeatId);
        Assert.Equal(ReservationStatus.Retained, attemptedReservation.Status);
        Assert.Equal(UtcNow.Add(RetentionDuration), attemptedReservation.ExpiresAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenExistingRequestMatches_ReturnsDuplicate()
    {
        var request = CreateRequest();
        var existingReservation = CreateExistingReservation(request);
        var repository = new StubReservationRepository();
        repository.EnqueueResult(ReservationInsertResult.RequestIdExists(existingReservation));
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessAsync(request);

        Assert.Equal(ReservationProcessingOutcome.Duplicate, result.Outcome);
        Assert.Equal(existingReservation.Status, result.CurrentStatus);
        Assert.Equal(existingReservation.RejectionReason, result.RejectionReason);
        Assert.Single(repository.AttemptedReservations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessAsync_WhenExistingRequestHasDifferentPayload_ReturnsConflict(
        bool changeEvent)
    {
        var request = CreateRequest();
        var existingReservation = CreateExistingReservation(request);
        if (changeEvent)
        {
            existingReservation.EventId = Guid.NewGuid();
        }
        else
        {
            existingReservation.SeatId = Guid.NewGuid();
        }

        var repository = new StubReservationRepository();
        repository.EnqueueResult(ReservationInsertResult.RequestIdExists(existingReservation));
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessAsync(request);

        Assert.Equal(ReservationProcessingOutcome.RequestConflict, result.Outcome);
        Assert.Equal(ReservationProcessingErrorCodes.RequestPayloadConflict, result.ErrorCode);
        Assert.Single(repository.AttemptedReservations);
    }

    [Fact]
    public async Task ProcessAsync_WhenSeatIsTaken_PersistsRejectedReservation()
    {
        var repository = new StubReservationRepository();
        repository.EnqueueResult(ReservationInsertResult.SeatTaken());
        repository.EnqueueResult(ReservationInsertResult.Created());
        var processor = CreateProcessor(repository);
        var request = CreateRequest();

        var result = await processor.ProcessAsync(request);

        Assert.Equal(ReservationProcessingOutcome.SeatTaken, result.Outcome);
        Assert.Equal(ReservationStatus.Rejected, result.CurrentStatus);
        Assert.Equal(ReservationRejectionReason.SeatTaken, result.RejectionReason);
        Assert.Equal(2, repository.AttemptedReservations.Count);

        var rejectedReservation = repository.AttemptedReservations[1];
        Assert.Equal(request.RequestId, rejectedReservation.Id);
        Assert.Equal(request.EventId, rejectedReservation.EventId);
        Assert.Equal(request.SeatId, rejectedReservation.SeatId);
        Assert.Equal(ReservationStatus.Rejected, rejectedReservation.Status);
        Assert.Equal(ReservationRejectionReason.SeatTaken, rejectedReservation.RejectionReason);
        Assert.Null(rejectedReservation.ExpiresAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenRejectedInsertFindsCompatibleRequest_ReturnsDuplicate()
    {
        var request = CreateRequest();
        var existingRejectedReservation = CreateExistingReservation(request);
        existingRejectedReservation.Status = ReservationStatus.Rejected;
        existingRejectedReservation.RejectionReason = ReservationRejectionReason.SeatTaken;
        existingRejectedReservation.ExpiresAt = null;

        var repository = new StubReservationRepository();
        repository.EnqueueResult(ReservationInsertResult.SeatTaken());
        repository.EnqueueResult(
            ReservationInsertResult.RequestIdExists(existingRejectedReservation));
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessAsync(request);

        Assert.Equal(ReservationProcessingOutcome.Duplicate, result.Outcome);
        Assert.Equal(ReservationStatus.Rejected, result.CurrentStatus);
        Assert.Equal(ReservationRejectionReason.SeatTaken, result.RejectionReason);
    }

    [Fact]
    public void Constructor_WhenRetentionDurationIsNotPositive_Throws()
    {
        var repository = new StubReservationRepository();
        var options = Options.Create(new ReservationProcessingOptions
        {
            RetentionDuration = TimeSpan.Zero
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ReservationRequestProcessor(repository, options, new FixedTimeProvider(UtcNow)));

        Assert.Contains(nameof(ReservationProcessingOptions.RetentionDuration), exception.Message);
    }

    private static ReservationRequestProcessor CreateProcessor(
        StubReservationRepository repository) =>
        new(
            repository,
            Options.Create(new ReservationProcessingOptions
            {
                RetentionDuration = RetentionDuration
            }),
            new FixedTimeProvider(UtcNow));

    private static ProcessReservationRequest CreateRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static Reservation CreateExistingReservation(ProcessReservationRequest request) =>
        new()
        {
            Id = request.RequestId,
            EventId = request.EventId,
            SeatId = request.SeatId,
            Status = ReservationStatus.Retained,
            ExpiresAt = UtcNow.Add(RetentionDuration)
        };
}
