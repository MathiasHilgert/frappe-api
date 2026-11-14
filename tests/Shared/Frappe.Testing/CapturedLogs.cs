using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Frappe.Testing;

/// <summary>Remembers every log entry: its category, level, rendered message and exception.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

    /// <summary>A typed logger writing here, for a class built by hand.</summary>
    public ILogger<T> For<T>() => new Logger<T>(new LoggerFactory([this]));

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, string, Exception?)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, logLevel, formatter(state, exception), exception));
    }
}
