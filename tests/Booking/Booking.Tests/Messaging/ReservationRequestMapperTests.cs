using Booking.Api.Messaging;

namespace Booking.Tests.Messaging;

public sealed class ReservationRequestMapperTests
{
    private readonly ReservationRequestMapper mapper = new();

    [Fact]
    public void Map_WhenProvisionalMessageIsValid_ReturnsBusinessCommand()
    {
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var delivery = CreateDelivery(requestId, eventId, seatId, requestId.ToString());

        var result = mapper.Map(delivery);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Request);
        Assert.Equal(requestId, result.Request.RequestId);
        Assert.Equal(eventId, result.Request.EventId);
        Assert.Equal(seatId, result.Request.SeatId);
    }

    [Fact]
    public void Map_WhenJsonIsInvalid_ReturnsInvalidJson()
    {
        var delivery = new ReservationRequestDelivery(
            BinaryData.FromString("{invalid-json"),
            Guid.NewGuid().ToString(),
            null,
            null);

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Equal(ReservationRequestMappingErrorCodes.InvalidJson, result.ErrorCode);
    }

    [Theory]
    [InlineData("requestId", ReservationRequestMappingErrorCodes.RequestIdRequired)]
    [InlineData("eventId", ReservationRequestMappingErrorCodes.EventIdRequired)]
    [InlineData("seatId", ReservationRequestMappingErrorCodes.SeatIdRequired)]
    public void Map_WhenIdentifierIsEmpty_ReturnsExpectedError(
        string emptyField,
        string expectedErrorCode)
    {
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        requestId = emptyField == "requestId" ? Guid.Empty : requestId;
        eventId = emptyField == "eventId" ? Guid.Empty : eventId;
        seatId = emptyField == "seatId" ? Guid.Empty : seatId;

        var delivery = CreateDelivery(
            requestId,
            eventId,
            seatId,
            requestId == Guid.Empty ? Guid.NewGuid().ToString() : requestId.ToString());

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
    }

    [Theory]
    [InlineData(null, ReservationRequestMappingErrorCodes.MessageIdRequired)]
    [InlineData("", ReservationRequestMappingErrorCodes.MessageIdRequired)]
    [InlineData("not-a-guid", ReservationRequestMappingErrorCodes.MessageIdInvalid)]
    public void Map_WhenMessageIdIsMissingOrInvalid_ReturnsExpectedError(
        string? messageId,
        string expectedErrorCode)
    {
        var delivery = CreateDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            messageId);

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
    }

    [Fact]
    public void Map_WhenMessageIdDoesNotMatchRequestId_ReturnsMismatch()
    {
        var delivery = CreateDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString());

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Equal(ReservationRequestMappingErrorCodes.MessageIdMismatch, result.ErrorCode);
    }

    private static ReservationRequestDelivery CreateDelivery(
        Guid requestId,
        Guid eventId,
        Guid seatId,
        string? messageId)
    {
        var body = BinaryData.FromString(
            $$"""
              {
                "requestId": "{{requestId}}",
                "eventId": "{{eventId}}",
                "seatId": "{{seatId}}"
              }
              """);

        return new ReservationRequestDelivery(
            body,
            messageId,
            "test-correlation-id",
            null);
    }
}
