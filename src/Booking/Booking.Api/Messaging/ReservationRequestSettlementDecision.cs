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
}
