using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Frappe.Geo.IntegrationTests.Addresses;

/// <summary>
/// Every log of a host as an exporter would see it: the message, its structured values and the values of every scope
/// it was written in (ASP.NET Core's <c>RequestPath</c> among them).
/// </summary>
internal sealed class ScopedLogs : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public sealed record Entry(string Category, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values, IReadOnlyList<KeyValuePair<string, object?>> Scopes);

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    /// <summary>Every message, value and scope value, one per line.</summary>
    public string Text() => string.Join("\n", Entries.SelectMany(e =>
        new[] { e.Message }.Concat(e.Values.Concat(e.Scopes).Select(v => $"{v.Key}={v.Value}"))));

    public void Dispose()
    {
        // Nothing to release: entries stay readable after the host stops.
    }

    private sealed class Logger(ScopedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            List<KeyValuePair<string, object?>> scopes = [];
            owner._scopes.ForEachScope((scope, list) => list.AddRange(ValuesOf(scope)), scopes);
            owner.Entries.Enqueue(new Entry(category, formatter(state, exception) + exception, ValuesOf(state), scopes));
        }

        private static List<KeyValuePair<string, object?>> ValuesOf(object? state) => state switch
        {
            IEnumerable<KeyValuePair<string, object?>> pairs => [.. pairs],
            null => [],
            _ => [new("scope", state.ToString())],
        };
    }
}
