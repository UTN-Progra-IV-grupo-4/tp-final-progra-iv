namespace Booking.Api.Logging;

// Audit events of Booking. The numeric id shows in the console as Category[id] and is
// stable: filter by it, not by the message text. Ids 2xxx belong to Booking.
internal static class BookingAuditEvents
{
    public const string ServiceName = "Booking";

    // Start of the chain in Booking: a reservation message was received from the broker.
    public static readonly EventId ReservationMessageReceived = new(2001, nameof(ReservationMessageReceived));

    // End of the chain in Booking: the message was completed; carries the final reservation state.
    public static readonly EventId ReservationMessageCompleted = new(2002, nameof(ReservationMessageCompleted));

    // End of the chain in Booking: the message went to the dead-letter queue; ErrorCode is the reason.
    public static readonly EventId ReservationMessageDeadLettered = new(2003, nameof(ReservationMessageDeadLettered));

    // Payment result unknown after bounded attempts (possible late charge).
    public static readonly EventId PossibleLatePayment = new(2004, nameof(PossibleLatePayment));

    // Payment approved after the reservation was no longer Retained (late charge).
    public static readonly EventId LateApprovedPayment = new(2005, nameof(LateApprovedPayment));

    // Processing or settlement failed; the broker will redeliver the message.
    public static readonly EventId ReservationMessageFailed = new(2006, nameof(ReservationMessageFailed));

    // Broker: this consumer received a message published from another host (Warning). Several consumers of
    // the same subscription compete for the messages, so another developer's Booking can take this one's.
    public static readonly EventId ReservationMessageFromOtherHost = new(2007, nameof(ReservationMessageFromOtherHost));

    // Payment: a charge attempt was sent to Payment.
    public static readonly EventId PaymentChargeRequested = new(2010, nameof(PaymentChargeRequested));

    // Payment: Payment answered Approved or Declined.
    public static readonly EventId PaymentChargeAnswered = new(2011, nameof(PaymentChargeAnswered));

    // Payment: the attempt failed (gRPC status or transport); ErrorCode is the failure kind.
    public static readonly EventId PaymentChargeFailed = new(2012, nameof(PaymentChargeFailed));

    // HTTP: a request carried (or lacked) the correlation id. Debug on purpose: status polling is not audit noise.
    public static readonly EventId HttpRequestReceived = new(2020, nameof(HttpRequestReceived));
}
