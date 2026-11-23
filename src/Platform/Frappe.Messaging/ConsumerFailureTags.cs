using Microsoft.Extensions.Diagnostics.Metrics;

namespace Frappe.Messaging;

/// <summary>Tags of <see cref="MessagingMetrics.ConsumerFailuresMetric"/>.</summary>
public readonly struct ConsumerFailureTags
{
    /// <summary>The consumer that failed.</summary>
    [TagName(MessagingMetrics.ConsumerTag)]
    public string Consumer { get; init; }

    /// <summary><c>subscription</c> or <c>after_commit</c>.</summary>
    [TagName(MessagingMetrics.ReasonTag)]
    public string Reason { get; init; }
}
