using System.Diagnostics;

namespace Frappe.Observability;

/// <summary>
/// Names of the telemetry the platform emits on behalf of every module.
/// Hosts subscribe to <c>Frappe.*</c>, so modules never register anything themselves.
/// </summary>
public static class FrappeTelemetry
{
    /// <summary>Name of the platform <see cref="ActivitySource"/> and meter.</summary>
    public const string Name = "Frappe.UseCases";

    /// <summary>Wildcard that matches the platform source and every module meter (<c>Frappe.&lt;Module&gt;</c>).</summary>
    public const string Wildcard = "Frappe.*";

    /// <summary>Histogram of command and query handler durations, in seconds.</summary>
    public const string HandlerDurationMetric = "frappe.handler.duration";

    internal static readonly ActivitySource Source = new(Name);

    public static class Tags
    {
        /// <summary><c>command</c> or <c>query</c>.</summary>
        public const string Kind = "frappe.handler.kind";

        /// <summary>The use case, named after its request type (for example <c>GetHolidays</c>).</summary>
        public const string UseCase = "frappe.use_case";

        /// <summary>The module, from the namespace convention <c>Frappe.&lt;Module&gt;.*</c>.</summary>
        public const string Module = "frappe.module";

        /// <summary><c>success</c>, <c>failure</c>, <c>cancelled</c> or <c>exception</c>.</summary>
        public const string Outcome = "frappe.outcome";

        /// <summary>Code of the first business error, when the outcome is <c>failure</c>.</summary>
        public const string ErrorCode = "frappe.error.code";
    }
}
