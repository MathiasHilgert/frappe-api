using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// The instrument an unsubscribe feeds, on the module's meter <c>Frappe.Notifications</c> (OBS-01, OBS-02). It counts once the commit that
/// recorded the opt-out landed, never for a rollback or a repeat (OBS-06). The tag value is a closed set named here.
/// </summary>
public sealed class PreferenceMetrics
{
    /// <summary>Counter: categories recipients turned off, by category.</summary>
    public const string UnsubscribesMetric = "frappe.notifications.unsubscribes";

    public const string CategoryTag = "category";

    private readonly Counter<long> _unsubscribes;

    public PreferenceMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _unsubscribes = meters.Create(NotificationsMetrics.MeterName).CreateCounter<long>(
            UnsubscribesMetric, unit: "{unsubscribe}", description: "Categories recipients turned off, by category.");
    }

    public void CountUnsubscribe(NotificationCategory category) =>
        _unsubscribes.Add(1, new KeyValuePair<string, object?>(CategoryTag, CategoryName(category)));

    /// <summary>The tag value of a category; each is named, none falls back (OBS-05).</summary>
    internal static string CategoryName(NotificationCategory category) => category switch
    {
        NotificationCategory.Security => "security",
        NotificationCategory.Transactional => "transactional",
        NotificationCategory.Marketing => "marketing",
        _ => throw new UnreachableException($"Unknown category {category}."),
    };
}
