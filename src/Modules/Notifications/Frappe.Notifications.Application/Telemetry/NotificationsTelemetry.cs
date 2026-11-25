using System.Diagnostics;

namespace Frappe.Notifications.Application.Telemetry;

/// <summary>
/// What the Notifications module's metrics classes share (OBS-01, OBS-02): the meter <c>Frappe.Notifications</c>, the tags more than one feature
/// uses, and the dispatcher's span on the source of the same name, which hosts export through <c>Frappe.*</c>. The span's tags are the
/// delivery counter's (kind, channel, outcome), never an address.
/// </summary>
public static class NotificationsTelemetry
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Notifications";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";

    /// <summary>The tag with the kind of a notification, a name of the catalog.</summary>
    public const string KindTag = "kind";

    /// <summary>The tag with the channel of a delivery, a name of the registered plug-ins.</summary>
    public const string ChannelTag = "channel";

    // Exception to OBS-01: the platform's handler span stops at the consumer; the dispatcher's per-channel span is a business span of this module.
    internal static readonly ActivitySource Source = new(MeterName);

    internal static Activity? StartDelivery(string kind, string channel)
    {
        var activity = Source.StartActivity("notifications.deliver", ActivityKind.Internal);
        activity?.SetTag(KindTag, kind);
        activity?.SetTag(ChannelTag, channel);
        return activity;
    }
}
