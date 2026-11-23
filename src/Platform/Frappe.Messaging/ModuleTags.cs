using Microsoft.Extensions.Diagnostics.Metrics;

namespace Frappe.Messaging;

/// <summary>Tag of <see cref="MessagingMetrics.RelayedMetric"/>.</summary>
public readonly struct ModuleTags
{
    /// <summary>The module whose relay published.</summary>
    [TagName(MessagingMetrics.ModuleTag)]
    public string Module { get; init; }
}
