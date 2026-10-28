using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Keeps every log record's level, EventId and structured state, so tests never read message text.
internal sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<Record> Records { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose() { }

    internal sealed record Record(
        LogLevel Level,
        EventId EventId,
        IReadOnlyDictionary<string, object?> State
    );

    private sealed class Logger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) =>
            capture.Records.Enqueue(
                new Record(
                    logLevel,
                    eventId,
                    // A template may name a placeholder twice; the last value wins instead of throwing
                    // into the code that logged.
                    state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                        ? pairs
                            .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
                            .ToDictionary(
                                static group => group.Key,
                                static group => group.Last().Value,
                                StringComparer.Ordinal
                            )
                        : new Dictionary<string, object?>(StringComparer.Ordinal)
                )
            );
    }
}
