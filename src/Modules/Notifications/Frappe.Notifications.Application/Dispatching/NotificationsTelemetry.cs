using System.Diagnostics;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// The span of one channel delivery, on the source <c>Frappe.Notifications</c>, which hosts export through <c>Frappe.*</c>. Its tags are the
/// delivery counter's (kind, channel, outcome), never an address.
/// </summary>
internal static class NotificationsTelemetry
{
    // Exception to OBS-01: the platform's handler span stops at the consumer; the dispatcher's per-channel span is a business span of this module.
    internal static readonly ActivitySource Source = new(NotificationsMetrics.MeterName);

    internal static Activity? StartDelivery(string kind, string channel)
    {
        var activity = Source.StartActivity("notifications.deliver", ActivityKind.Internal);
        activity?.SetTag(NotificationsMetrics.KindTag, kind);
        activity?.SetTag(NotificationsMetrics.ChannelTag, channel);
        return activity;
    }
}
