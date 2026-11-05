using System.Text.Json;
using Booking.Business.Reservations;
using Contracts;

namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMapper
{
    public ReservationRequestMappingResult Map(ReservationRequestDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        ReservationRequestMessage? message;
        try
        {
            message = delivery.Body.ToObjectFromJson<ReservationRequestMessage>();
        }
        catch (JsonException)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.InvalidJson);
        }

        if (message is null)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.InvalidJson);
        }

        if (message.MessageId == Guid.Empty)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.PayloadMessageIdRequired);
        }

        if (message.EventId == Guid.Empty)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.EventIdRequired);
        }

        if (message.SeatId == Guid.Empty)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.SeatIdRequired);
        }

        if (string.IsNullOrWhiteSpace(delivery.MessageId))
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.MessageIdRequired);
        }

        if (!Guid.TryParse(delivery.MessageId, out var messageId))
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.MessageIdInvalid);
        }

        if (messageId != message.MessageId)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.MessageIdMismatch);
        }

        if (string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.CorrelationIdRequired);
        }

        if (!IsSafeCorrelationId(message.CorrelationId))
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.CorrelationIdInvalid);
        }

        return ReservationRequestMappingResult.Success(
            new ProcessReservationRequest(
                message.MessageId,
                message.EventId,
                message.SeatId),
            message.CorrelationId);
    }

    internal static bool IsSafeCorrelationId(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(character => character is
            (>= 'a' and <= 'z') or
            (>= 'A' and <= 'Z') or
            (>= '0' and <= '9') or '-' or '_' or '.');
}
