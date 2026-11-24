using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// The Notifications module's instruments on meter <c>Frappe.Notifications</c> (OBS-01, OBS-02), created once through
/// <see cref="IMeterFactory"/>. Tags are bounded: the kind comes from the catalog, the channel from the registered plug-ins.
/// </summary>
public sealed class NotificationsMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Notifications";

    /// <summary>Counter: intended deliveries, by kind, channel and outcome.</summary>
    public const string DeliveriesMetric = "frappe.notifications.deliveries";

    public const string KindTag = "kind";

    public const string ChannelTag = "channel";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";

    private readonly Counter<long> _deliveries;

    public NotificationsMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);
        _deliveries = meter.CreateCounter<long>(
            DeliveriesMetric, unit: "{delivery}", description: "Intended notification deliveries, by kind, channel and outcome.");
    }

    internal void Delivery(string kind, string channel, DispatchOutcome outcome) =>
        _deliveries.Add(1, new(KindTag, kind), new(ChannelTag, channel), new(OutcomeTag, OutcomeName(outcome)));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(DispatchOutcome outcome) => outcome switch
    {
        DispatchOutcome.Sent => "sent",
        DispatchOutcome.Failed => "failed",
        DispatchOutcome.Unresolved => "unresolved",
        DispatchOutcome.OptedOut => "opted_out",
        DispatchOutcome.Suppressed => "suppressed",
        DispatchOutcome.Duplicate => "duplicate",
        DispatchOutcome.NoChannel => "no_channel",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
