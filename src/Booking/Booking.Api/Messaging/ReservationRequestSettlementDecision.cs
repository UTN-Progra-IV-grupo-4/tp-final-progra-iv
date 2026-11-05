namespace Booking.Api.Messaging;

internal enum ReservationRequestSettlementAction
{
    Complete,
    DeadLetter
}

internal sealed record ReservationRequestSettlementDecision(
    ReservationRequestSettlementAction Action,
    string? Reason = null,
    string? Description = null);

internal static class ReservationRequestSettlementErrorCodes
{
    public const string InvalidRequest = "InvalidRequest";
    public const string PaymentPending = "PaymentPending";
    public const string PaymentInvalidRequest = "PaymentInvalidRequest";
    public const string PaymentUnexpectedResponse = "PaymentUnexpectedResponse";
    public const string PaymentApprovedAfterExpiry = "PaymentApprovedAfterExpiry";
}
