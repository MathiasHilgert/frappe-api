using Microsoft.Extensions.Diagnostics.Metrics;

namespace Frappe.Messaging;

/// <summary>Tags of <see cref="MessagingMetrics.ConsumedMetric"/>.</summary>
public readonly struct ConsumedTags
{
    /// <summary>The consumer that settled the delivery.</summary>
    [TagName(MessagingMetrics.ConsumerTag)]
    public string Consumer { get; init; }

    /// <summary><c>handled</c>, <c>duplicate</c>, <c>failed</c> or <c>dead_lettered</c>.</summary>
    [TagName(MessagingMetrics.OutcomeTag)]
    public string Outcome { get; init; }
}
