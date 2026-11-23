using Microsoft.Extensions.Diagnostics.Metrics;

namespace Frappe.Messaging;

/// <summary>Tags of <see cref="MessagingMetrics.PurgedMetric"/>.</summary>
public readonly struct PurgedTags
{
    /// <summary>The module whose rows were purged.</summary>
    [TagName(MessagingMetrics.ModuleTag)]
    public string Module { get; init; }

    /// <summary>The table the rows came from.</summary>
    [TagName(MessagingMetrics.TableTag)]
    public string Table { get; init; }
}
