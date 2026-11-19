using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>A logger provider that hands every log entry to a telemetry capture.</summary>
internal sealed class CaptureLoggerProvider(TelemetryCapture capture) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(capture);

    public void Dispose()
    {
    }

    /// <summary>A logger that records its entries in a telemetry capture.</summary>
    private sealed class CaptureLogger(TelemetryCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture.Logs.Add((logLevel, formatter(state, exception)));
    }
}
