using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// The instruments the provider's feedback feeds, on the module's meter <c>Frappe.Notifications</c> (OBS-01, OBS-02). They are counted once
/// the commit that recorded the event landed, never for a rollback (OBS-06). Tag values are closed sets named here, never the provider's text.
/// </summary>
public sealed class ProviderFeedbackMetrics
{
    /// <summary>Counter: events the webhook recorded, by what they say and whether their delivery was found.</summary>
    public const string ProviderEventsMetric = "frappe.notifications.provider_events";

    /// <summary>Counter: addresses suppressed, by reason.</summary>
    public const string SuppressionsMetric = "frappe.notifications.suppressions";

    public const string TypeTag = "type";

    public const string MatchedTag = "matched";

    public const string ReasonTag = "reason";

    private readonly Counter<long> _providerEvents;

    private readonly Counter<long> _suppressions;

    public ProviderFeedbackMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(NotificationsMetrics.MeterName);
        _providerEvents = meter.CreateCounter<long>(
            ProviderEventsMetric, unit: "{event}", description: "Provider events recorded, by type and whether their delivery was found.");
        _suppressions = meter.CreateCounter<long>(
            SuppressionsMetric, unit: "{suppression}", description: "Addresses suppressed, by reason.");
    }

    public void CountProviderEvent(DeliveryFeedback feedback, bool matched) =>
        _providerEvents.Add(1, new(TypeTag, TypeName(feedback)), new(MatchedTag, matched));

    public void CountSuppression(SuppressionReason reason) => _suppressions.Add(1, new KeyValuePair<string, object?>(ReasonTag, ReasonName(reason)));

    /// <summary>The tag value of what a provider said; each is named, none falls back (OBS-05).</summary>
    internal static string TypeName(DeliveryFeedback feedback) => feedback switch
    {
        DeliveryFeedback.Delivered => "delivered",
        DeliveryFeedback.PermanentFailure => "permanent_failure",
        DeliveryFeedback.Complaint => "complaint",
        DeliveryFeedback.Delayed => "delayed",
        DeliveryFeedback.SuppressedByProvider => "suppressed_by_provider",
        _ => throw new UnreachableException($"Unknown feedback {feedback}."),
    };

    /// <summary>The tag value of a suppression reason.</summary>
    internal static string ReasonName(SuppressionReason reason) => reason switch
    {
        SuppressionReason.HardBounce => "hard_bounce",
        SuppressionReason.Complaint => "complaint",
        SuppressionReason.ProviderSuppression => "provider_suppression",
        _ => throw new UnreachableException($"Unknown reason {reason}."),
    };
}
