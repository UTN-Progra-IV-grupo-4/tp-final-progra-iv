using Booking.Api.Messaging;
using Booking.Business.Reservations;

namespace Booking.Tests.Messaging;

public sealed class ReservationRequestSettlementDeciderTests
{
    public static TheoryData<string> MappingErrorCodes =>
        new()
        {
            ReservationRequestMappingErrorCodes.InvalidJson,
            ReservationRequestMappingErrorCodes.RequestIdRequired,
            ReservationRequestMappingErrorCodes.EventIdRequired,
            ReservationRequestMappingErrorCodes.SeatIdRequired,
            ReservationRequestMappingErrorCodes.MessageIdRequired,
            ReservationRequestMappingErrorCodes.MessageIdInvalid,
            ReservationRequestMappingErrorCodes.MessageIdMismatch
        };

    [Theory]
    [InlineData(ReservationProcessingOutcome.Retained)]
    [InlineData(ReservationProcessingOutcome.Duplicate)]
    [InlineData(ReservationProcessingOutcome.SeatTaken)]
    public void Decide_WhenBusinessOutcomeIsSuccessful_Completes(
        ReservationProcessingOutcome outcome)
    {
        var handlingResult = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(outcome, Guid.NewGuid()));

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);

        Assert.Equal(ReservationRequestSettlementAction.Complete, decision.Action);
        Assert.Null(decision.Reason);
        Assert.Null(decision.Description);
    }

    [Theory]
    [MemberData(nameof(MappingErrorCodes))]
    public void Decide_WhenMapperReturnsDeterministicError_DeadLettersWithSameCode(
        string errorCode)
    {
        var handlingResult = ReservationRequestHandlingResult.Invalid(errorCode);

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);

        Assert.Equal(ReservationRequestSettlementAction.DeadLetter, decision.Action);
        Assert.Equal(errorCode, decision.Reason);
        Assert.False(string.IsNullOrWhiteSpace(decision.Description));
    }

    [Fact]
    public void Decide_WhenRequestConflicts_DeadLettersWithConflictCode()
    {
        var handlingResult = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(
                ReservationProcessingOutcome.RequestConflict,
                Guid.NewGuid(),
                ErrorCode: ReservationProcessingErrorCodes.RequestPayloadConflict));

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);

        Assert.Equal(ReservationRequestSettlementAction.DeadLetter, decision.Action);
        Assert.Equal(
            ReservationProcessingErrorCodes.RequestPayloadConflict,
            decision.Reason);
        Assert.False(string.IsNullOrWhiteSpace(decision.Description));
    }

    [Fact]
    public void Decide_WhenBusinessRequestIsInvalid_DeadLettersWithBusinessCode()
    {
        var handlingResult = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(
                ReservationProcessingOutcome.InvalidRequest,
                Guid.NewGuid(),
                ErrorCode: ReservationProcessingErrorCodes.EventIdRequired));

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);

        Assert.Equal(ReservationRequestSettlementAction.DeadLetter, decision.Action);
        Assert.Equal(ReservationProcessingErrorCodes.EventIdRequired, decision.Reason);
        Assert.False(string.IsNullOrWhiteSpace(decision.Description));
    }

    [Fact]
    public void Decide_WhenBusinessRequestIsInvalidWithoutCode_UsesFallbackCode()
    {
        var handlingResult = ReservationRequestHandlingResult.Processed(
            new ReservationProcessingResult(
                ReservationProcessingOutcome.InvalidRequest,
                Guid.NewGuid()));

        var decision = ReservationRequestSettlementDecider.Decide(handlingResult);

        Assert.Equal(ReservationRequestSettlementAction.DeadLetter, decision.Action);
        Assert.Equal(ReservationRequestSettlementErrorCodes.InvalidRequest, decision.Reason);
    }
}
