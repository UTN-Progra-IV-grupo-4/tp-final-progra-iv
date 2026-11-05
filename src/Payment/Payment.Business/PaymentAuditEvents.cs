using Microsoft.Extensions.Logging;

namespace Payment.Business;

// Audit events of the charge chain (ticket #24). The numeric id shows in the console as Category[id]
// and is stable: filter by it, not by the message text. Ids 3xxx belong to Payment.
public static class PaymentAuditEvents
{
    // Start of the chain in Payment: a charge request arrived (retries included).
    public static readonly EventId ChargeStarted = new(3001, nameof(ChargeStarted));

    // End of the chain: the gateway answered and the result was saved.
    public static readonly EventId ChargeFinished = new(3002, nameof(ChargeFinished));

    // End of the chain for a retry: answered with the stored charge, no new gateway call.
    public static readonly EventId ChargeReplayed = new(3003, nameof(ChargeReplayed));

    // End of the chain with an error: the gRPC status code is the ErrorCode.
    public static readonly EventId ChargeRejected = new(3004, nameof(ChargeRejected));
}
