using Microsoft.Extensions.Logging;

namespace Availability.Tests;

// Keeps every entry (level, event id, rendered message, exception) so audit tests can assert on them.
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new RecordedLogEntry(logLevel, eventId, formatter(state, exception), exception));
}

public sealed record RecordedLogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
