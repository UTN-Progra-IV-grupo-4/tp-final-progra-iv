using Booking.Api.Messaging;
using Contracts;
using System.Text.Json;

namespace Booking.Tests.Messaging;

public sealed class ReservationRequestMapperTests
{
    private readonly ReservationRequestMapper mapper = new();

    [Fact]
    public void Map_WhenSharedContractMessageIsValid_ReturnsBusinessCommand()
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
        Assert.Equal("test-correlation-id", result.CorrelationId);
    }

    [Fact]
    public void SharedContract_SerializesPublisherFieldsInCamelCase()
    {
        var requestId = Guid.NewGuid();
        var delivery = CreateDelivery(requestId, Guid.NewGuid(), Guid.NewGuid(), requestId.ToString(),
            requestId.ToString());

        using var json = JsonDocument.Parse(delivery.Body.ToString());
        var payload = json.RootElement;
        Assert.Equal(requestId.ToString(), payload.GetProperty("messageId").GetGuid().ToString());
        Assert.Equal(requestId.ToString(), payload.GetProperty("correlationId").GetString());
        Assert.True(payload.TryGetProperty("eventId", out _));
        Assert.True(payload.TryGetProperty("seatId", out _));
        Assert.True(payload.TryGetProperty("userId", out _));
        Assert.True(payload.TryGetProperty("requestedAt", out _));
        Assert.False(payload.TryGetProperty("MessageId", out _));
        Assert.True(mapper.Map(delivery).IsValid);
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
    [InlineData("messageId", ReservationRequestMappingErrorCodes.PayloadMessageIdRequired)]
    [InlineData("eventId", ReservationRequestMappingErrorCodes.EventIdRequired)]
    [InlineData("seatId", ReservationRequestMappingErrorCodes.SeatIdRequired)]
    public void Map_WhenIdentifierIsEmpty_ReturnsExpectedError(
        string emptyField,
        string expectedErrorCode)
    {
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        requestId = emptyField == "messageId" ? Guid.Empty : requestId;
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
    public void Map_WhenBrokerMessageIdDoesNotMatchPayloadMessageId_ReturnsMismatch()
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

    [Fact]
    public void Map_WhenPayloadCorrelationIdIsMissing_ReturnsRequiredError()
    {
        var requestId = Guid.NewGuid();
        var delivery = CreateDelivery(
            requestId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            requestId.ToString(),
            correlationId: " ");

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Equal(ReservationRequestMappingErrorCodes.CorrelationIdRequired, result.ErrorCode);
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("non-ascii-é")]
    [InlineData("with space")]
    public void Map_WhenPayloadCorrelationIdHasUnsafeCharacters_ReturnsInvalidError(string correlationId)
    {
        var requestId = Guid.NewGuid();
        var delivery = CreateDelivery(requestId, Guid.NewGuid(), Guid.NewGuid(),
            requestId.ToString(), correlationId);

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Equal(ReservationRequestMappingErrorCodes.CorrelationIdInvalid, result.ErrorCode);
    }

    [Fact]
    public void Map_WhenPayloadCorrelationIdExceedsLimit_ReturnsInvalidError()
    {
        var requestId = Guid.NewGuid();
        var delivery = CreateDelivery(requestId, Guid.NewGuid(), Guid.NewGuid(),
            requestId.ToString(), new string('a', 129));

        Assert.Equal(ReservationRequestMappingErrorCodes.CorrelationIdInvalid,
            mapper.Map(delivery).ErrorCode);
    }

    [Fact]
    public void Map_WhenBodyIsSerializedAsAvailabilityPublishesIt_ReturnsBusinessCommand()
    {
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var published = new ReservationRequestMessage
        {
            MessageId = requestId,
            CorrelationId = requestId.ToString(),
            EventId = eventId,
            SeatId = seatId,
            UserId = "user-42",
            RequestedAt = DateTimeOffset.UtcNow.UtcDateTime
        };
        var body = BinaryData.FromString(JsonSerializer.Serialize(published));
        var delivery = new ReservationRequestDelivery(
            body,
            requestId.ToString(),
            requestId.ToString(),
            null);

        var result = mapper.Map(delivery);

        Assert.True(result.IsValid);
        Assert.Equal(requestId, result.Request!.RequestId);
        Assert.Equal(eventId, result.Request.EventId);
        Assert.Equal(seatId, result.Request.SeatId);
    }

    [Fact]
    public void Map_WhenBodyIsTheDocumentedContractExample_ReturnsBusinessCommand()
    {
        var delivery = new ReservationRequestDelivery(
            BinaryData.FromString(
                """
                {
                  "messageId": "7b2f0c31-9d3a-4a4f-8d2e-0f6b11f5a245",
                  "correlationId": "req-2026-10-31-001",
                  "eventId": "8cc3b719-4866-4f8b-a893-6db7d0f18b55",
                  "seatId": "1cf6fb92-e01e-4d7c-8935-14a42f8fd17a",
                  "userId": "user-42",
                  "requestedAt": "2026-10-31T12:00:00Z"
                }
                """),
            "7b2f0c31-9d3a-4a4f-8d2e-0f6b11f5a245",
            "req-2026-10-31-001",
            null);

        var result = mapper.Map(delivery);

        Assert.True(result.IsValid);
        Assert.Equal(Guid.Parse("7b2f0c31-9d3a-4a4f-8d2e-0f6b11f5a245"), result.Request!.RequestId);
        Assert.Equal(Guid.Parse("8cc3b719-4866-4f8b-a893-6db7d0f18b55"), result.Request.EventId);
        Assert.Equal(Guid.Parse("1cf6fb92-e01e-4d7c-8935-14a42f8fd17a"), result.Request.SeatId);
    }

    [Fact]
    public void Map_WhenBodyHasUnknownProperties_IgnoresThem()
    {
        var requestId = Guid.NewGuid();
        var delivery = new ReservationRequestDelivery(
            BinaryData.FromString(
                $$"""
                {
                  "messageId": "{{requestId}}",
                  "correlationId": "{{requestId}}",
                  "eventId": "{{Guid.NewGuid()}}",
                  "seatId": "{{Guid.NewGuid()}}",
                  "userId": "user-42",
                  "requestedAt": "2026-10-31T12:00:00Z",
                  "futureField": { "nested": true }
                }
                """),
            requestId.ToString(),
            requestId.ToString(),
            null);

        Assert.True(mapper.Map(delivery).IsValid);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    [InlineData("{\"messageId\":\"not-a-guid\"}")]
    [InlineData("{\"MessageId\":\"7b2f0c31-9d3a-4a4f-8d2e-0f6b11f5a245\"}")]
    public void Map_WhenBodyIsNotAContractObject_ReturnsInvalidJsonOrRequiredIdentifier(string body)
    {
        var delivery = new ReservationRequestDelivery(
            BinaryData.FromString(body),
            Guid.NewGuid().ToString(),
            null,
            null);

        var result = mapper.Map(delivery);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.ErrorCode,
            new[]
            {
                ReservationRequestMappingErrorCodes.InvalidJson,
                ReservationRequestMappingErrorCodes.PayloadMessageIdRequired
            });
    }

    private static ReservationRequestDelivery CreateDelivery(
        Guid requestId,
        Guid eventId,
        Guid seatId,
        string? messageId,
        string correlationId = "test-correlation-id")
    {
        var body = BinaryData.FromObjectAsJson(new ReservationRequestMessage
        {
            MessageId = requestId,
            CorrelationId = correlationId,
            EventId = eventId,
            SeatId = seatId,
            UserId = "test-user",
            RequestedAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)
        });

        return new ReservationRequestDelivery(
            body,
            messageId,
            "test-correlation-id",
            null);
    }
}