using System.Text.Json;
using Booking.Business.Reservations;

namespace Booking.Api.Messaging;

internal sealed class ReservationRequestMapper
{
    public ReservationRequestMappingResult Map(ReservationRequestDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        ProvisionalReservationRequestMessage? message;
        try
        {
            message = delivery.Body.ToObjectFromJson<ProvisionalReservationRequestMessage>();
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

        if (message.RequestId == Guid.Empty)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.RequestIdRequired);
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

        if (messageId != message.RequestId)
        {
            return ReservationRequestMappingResult.Invalid(
                ReservationRequestMappingErrorCodes.MessageIdMismatch);
        }

        return ReservationRequestMappingResult.Success(
            new ProcessReservationRequest(
                message.RequestId,
                message.EventId,
                message.SeatId));
    }
}
