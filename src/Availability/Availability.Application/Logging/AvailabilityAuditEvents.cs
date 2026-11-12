using Microsoft.Extensions.Logging;

namespace Availability.Application.Logging;

// Audit events of Availability. The numeric id shows in the console as Category[id] and is
// stable: filter by it, not by the message text. Ids 1xxx belong to Availability.
public static class AvailabilityAuditEvents
{
    public const string ServiceName = "Availability";

    // Start of the chain: the request passed validation and was stored. The request id is generated here and
    // is used as MessageId and CorrelationId of the broker message.
    public static readonly EventId ReservationRequestStored = new(1001, nameof(ReservationRequestStored));

    // The broker accepted the message: from here on the chain continues in Booking.
    public static readonly EventId ReservationRequestPublished = new(1002, nameof(ReservationRequestPublished));

    // The broker rejected the message; the stored request was removed. ErrorCode is PublishFailed.
    public static readonly EventId ReservationRequestPublishFailed = new(1003, nameof(ReservationRequestPublishFailed));

    // The request was published but Booking has not registered it after a while (Warning). The message may be
    // waiting in the broker, consumed by another consumer of the same subscription, or Booking may be down.
    public static readonly EventId ReservationRequestNotRegistered = new(1004, nameof(ReservationRequestNotRegistered));
}
