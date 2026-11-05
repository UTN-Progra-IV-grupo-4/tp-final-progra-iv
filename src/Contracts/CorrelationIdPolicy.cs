namespace Contracts;

// Single definition of how the correlation id travels between services: the HTTP header
// and gRPC metadata name, and the rule every receiver applies before putting the value in a log.
public static class CorrelationIdPolicy
{
    // gRPC metadata keys must be lowercase; HTTP headers are case-insensitive, so one name serves both.
    public const string HeaderName = "x-correlation-id";

    // Roomy for a GUID (36) or a prefixed id; a longer value is not a correlation id.
    public const int MaxLength = 64;

    // Marker logged in place of a value that failed the rule.
    public const string InvalidValue = "invalido";

    // The value comes from outside and ends up in every log line of the request: an unbounded or
    // control-character value would flood the logs or forge entries. A value that fails the rule is
    // replaced, never truncated: a cut value looks valid but no longer matches the sender's, which
    // confuses a cross-service search more than an explicit marker. Callers must never reject the
    // request because of it. Returns null when there is no value.
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var isValid = value.Length <= MaxLength &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

        return isValid ? value : InvalidValue;
    }
}
