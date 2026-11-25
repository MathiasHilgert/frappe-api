using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Notifications.Application.Dispatching;

namespace Frappe.Notifications.Application.Telemetry;

/// <summary>
/// The Dispatching feature's instrument on meter <c>Frappe.Notifications</c> (OBS-01, OBS-02), created once through
/// <see cref="IMeterFactory"/>. Tags are bounded: the kind comes from the catalog, the channel from the registered plug-ins.
/// </summary>
public sealed class NotificationsMetrics
{
    /// <summary>Counter: intended deliveries, by kind, channel and outcome.</summary>
    public const string DeliveriesMetric = "frappe.notifications.deliveries";

    private readonly Counter<long> _deliveries;

    public NotificationsMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(NotificationsTelemetry.MeterName);
        _deliveries = meter.CreateCounter<long>(
            DeliveriesMetric, unit: "{delivery}", description: "Intended notification deliveries, by kind, channel and outcome.");
    }

    internal void Delivery(string kind, string channel, DispatchOutcome outcome) =>
        _deliveries.Add(1, new(NotificationsTelemetry.KindTag, kind), new(NotificationsTelemetry.ChannelTag, channel),
            new(NotificationsTelemetry.OutcomeTag, OutcomeName(outcome)));

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
