using Frappe.Aggregates;
using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Domain.ProviderEvents;

/// <summary>
/// A provider event the webhook already handled, so a redelivery does nothing twice. An event that arrived before its delivery exists is
/// parked with what is needed to apply it when the delivery is recorded (Flow 3). It holds no address and no payload (S13).
/// </summary>
public sealed class ProviderEvent : AggregateRoot
{
    /// <summary>The longest provider event id stored.</summary>
    public const int EventIdLength = 100;

    private ProviderEvent() => EventId = null!;

    private ProviderEvent(DeliveryProvider provider, string eventId, bool matched, Instant now)
    {
        Provider = provider;
        EventId = eventId;
        Matched = matched;
        ReceivedAt = now;
    }

    public DeliveryProvider Provider { get; private set; }

    /// <summary>The provider's id of the event.</summary>
    public string EventId { get; private set; }

    public Instant ReceivedAt { get; private set; }

    /// <summary>Whether a delivery was found for the event when it arrived.</summary>
    public bool Matched { get; private set; }

    /// <summary>For a parked event: the provider's message id it is about.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>For a parked event: what the provider said.</summary>
    public DeliveryFeedback? Feedback { get; private set; }

    /// <summary>For a parked event: when the provider says it happened.</summary>
    public Instant? OccurredAt { get; private set; }

    /// <summary>An event handled against its delivery (or one about no message, which nothing can match).</summary>
    public static ProviderEvent Handled(DeliveryProvider provider, string eventId, DeliveryFeedback feedback, bool matched, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var recorded = new ProviderEvent(provider, eventId, matched, now);
        recorded.Raise(new ProviderEventRecorded(feedback, matched));
        return recorded;
    }

    /// <summary>An event whose delivery is not recorded yet: kept to be applied when it is.</summary>
    public static ProviderEvent Parked(
        DeliveryProvider provider, string eventId, string providerMessageId, DeliveryFeedback feedback, Instant occurredAt, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        var recorded = new ProviderEvent(provider, eventId, matched: false, now)
        {
            ProviderMessageId = providerMessageId,
            Feedback = feedback,
            OccurredAt = occurredAt,
        };
        recorded.Raise(new ProviderEventRecorded(feedback, Matched: false));
        return recorded;
    }
}
