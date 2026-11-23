using Microsoft.Extensions.Diagnostics.Metrics;

namespace Frappe.Messaging;

/// <summary>Tags of <see cref="MessagingMetrics.RelayFailuresMetric"/>.</summary>
public readonly struct RelayFailureTags
{
    /// <summary>The module whose relay failed.</summary>
    [TagName(MessagingMetrics.ModuleTag)]
    public string Module { get; init; }

    /// <summary><c>rejected</c>, <c>parked</c> or <c>unavailable</c>.</summary>
    [TagName(MessagingMetrics.OutcomeTag)]
    public string Outcome { get; init; }
}
