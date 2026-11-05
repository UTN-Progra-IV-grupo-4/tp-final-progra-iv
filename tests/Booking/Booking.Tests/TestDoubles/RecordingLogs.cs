using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Booking.Tests.TestDoubles;

// Real logger factory with a provider that keeps every entry together with the scopes open when it was
// written. Scopes are shared across categories like in production, so a test can check that a line written
// by one class carries the scope opened by another (the correlation id travels in a scope).
public sealed class RecordingLogs : IDisposable
{
    private readonly RecordingProvider provider = new();
    private readonly ILoggerFactory factory;

    public RecordingLogs() =>
        factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));

    // For hosts under test: adding it to the host's logging records what the real pipeline writes.
    public ILoggerProvider Provider => provider;

    public IReadOnlyList<RecordedLogEntry> Entries => provider.Entries.ToArray();

    public ILogger<T> CreateLogger<T>() => factory.CreateLogger<T>();

    public void Dispose() => factory.Dispose();

    private sealed class RecordingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

        public ConcurrentQueue<RecordedLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => this.scopeProvider = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(string category, RecordingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner.scopeProvider.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var scopes = new List<string>();
                owner.scopeProvider.ForEachScope((scope, list) => list.Add(scope?.ToString() ?? string.Empty), scopes);
                owner.Entries.Enqueue(new RecordedLogEntry(
                    category, logLevel, eventId, formatter(state, exception), exception, scopes.ToArray()));
            }
        }
    }
}

public sealed record RecordedLogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    string[] Scopes);
