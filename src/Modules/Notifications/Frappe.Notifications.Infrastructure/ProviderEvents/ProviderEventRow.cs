using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

// EF Core materializes the row through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Infrastructure.ProviderEvents;

/// <summary>
/// A provider event the webhook already handled, so a redelivery does nothing twice; an event that arrived before its delivery
/// exists is parked here with what is needed to apply it later (STR-08: data with no behavior of its own).
/// </summary>
internal sealed class ProviderEventRow
{
    private ProviderEventRow() => EventId = null!;

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
}
