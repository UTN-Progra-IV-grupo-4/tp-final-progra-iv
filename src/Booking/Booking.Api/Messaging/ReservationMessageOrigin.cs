using Contracts;

namespace Booking.Api.Messaging;

// Reads where a broker message was published from. Diagnostic only: the result never changes how a
// message is processed, it only feeds a log line.
internal static class ReservationMessageOrigin
{
    // Null when the publisher did not send the property (an older publisher) or the value is not a
    // plain host name. The value comes from outside and ends up in a log line, so it follows the same
    // rule as the correlation id and a value that fails it is ignored instead of logged.
    public static string? ReadPublisherHost(IReadOnlyDictionary<string, object> applicationProperties)
    {
        ArgumentNullException.ThrowIfNull(applicationProperties);

        if (!applicationProperties.TryGetValue(BrokerMessageProperties.PublisherHost, out var value) ||
            value is not string host)
        {
            return null;
        }

        var sanitizedHost = CorrelationIdPolicy.Sanitize(host);
        return sanitizedHost == CorrelationIdPolicy.InvalidValue ? null : sanitizedHost;
    }

    // A message without a known publisher is never reported: warning about it would only be noise.
    public static bool IsPublishedFromAnotherHost(string? publisherHost, string consumerHost) =>
        publisherHost is not null &&
        !string.Equals(publisherHost, consumerHost, StringComparison.OrdinalIgnoreCase);
}
