using Frappe.Aggregates;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Domain.ProviderEvents;

/// <summary>A provider event was recorded; <paramref name="Matched"/> says whether its delivery existed. Counted after the commit (S15).</summary>
/// <param name="Feedback">What the provider said.</param>
/// <param name="Matched">Whether a delivery was found for it when it arrived.</param>
public sealed record ProviderEventRecorded(DeliveryFeedback Feedback, bool Matched) : IDomainEvent;
