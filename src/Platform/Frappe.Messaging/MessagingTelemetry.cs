using System.Diagnostics;

namespace Frappe.Messaging;

/// <summary>Spans of publishing and consuming integration events. Hosts subscribe through <c>Frappe.*</c>.</summary>
public static class MessagingTelemetry
{
    public const string Name = "Frappe.Messaging";

    /// <summary>
    /// Sources a host must listen to together. NATS.Net writes its own publish span into the <c>traceparent</c>
    /// header; if NATS.Net is recorded but <see cref="Name"/> is not, that span points to a parent nobody exported
    /// and the trace tree has a hole. Registering both, always, keeps the chain save → publish → consume intact.
    /// </summary>
    public static readonly IReadOnlyList<string> ActivitySources = [Name, "NATS.Net"];

    internal static readonly ActivitySource Source = new(Name);

    /// <summary>Starts a span as a child of a stored or received W3C trace context, when there is one.</summary>
    internal static Activity? StartChildOf(string name, ActivityKind kind, string? traceParent, string? traceState) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var parent)
            ? Source.StartActivity(name, kind, parent)
            : Source.StartActivity(name, kind);
}
