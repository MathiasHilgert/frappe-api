using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frappe.Access.Application.Telemetry;

/// <summary>
/// The Access module's instruments on meter <c>Frappe.Access</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>. Tags are
/// bounded: the request is a type name of the code base, the outcome one of <see cref="AccessOutcome"/>; no id, email or rule text (S10).
/// </summary>
internal sealed class AccessMetrics
{
    /// <summary>Counter: access decisions, by request and outcome.</summary>
    public const string DecisionsMetric = "frappe.access.decisions";

    /// <summary>The tag with the name of the request (a command or query type) the decision was about.</summary>
    public const string RequestTag = "request";

    private readonly Counter<long> _decisions;

    public AccessMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _decisions = meters.Create(AccessTelemetry.MeterName).CreateCounter<long>(
            DecisionsMetric, unit: "{decision}", description: "Access decisions, by request and outcome.");
    }

    /// <summary>A decision about <paramref name="request"/> ended with <paramref name="outcome"/>.</summary>
    public void Decided(string request, AccessOutcome outcome) =>
        _decisions.Add(
            1,
            new KeyValuePair<string, object?>(RequestTag, request),
            new KeyValuePair<string, object?>(AccessTelemetry.OutcomeTag, OutcomeName(outcome)));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(AccessOutcome outcome) => outcome switch
    {
        AccessOutcome.Allowed => "allowed",
        AccessOutcome.Hidden => "hidden",
        AccessOutcome.Forbidden => "forbidden",
        AccessOutcome.StepUpRequired => "step_up_required",
        AccessOutcome.Public => "public",
        AccessOutcome.Undeclared => "undeclared",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
