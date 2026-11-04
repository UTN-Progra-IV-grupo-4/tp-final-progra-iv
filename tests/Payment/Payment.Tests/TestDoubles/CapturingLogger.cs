using Microsoft.Extensions.Logging;

namespace Payment.Tests.TestDoubles;

// Logger que registra el CorrelationId de cada scope abierto: es la forma observable de comprobar
// qué valor llegó a los logs sin acceder a los métodos privados del servicio.
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string?> CorrelationIds { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach (var property in properties)
            {
                if (property.Key == "CorrelationId")
                    CorrelationIds.Add(property.Value as string);
            }
        }

        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
