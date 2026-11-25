using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.Application.ProviderEvents;

/// <summary>What the provider said about a message before its delivery was recorded, and when.</summary>
public sealed record ParkedFeedback(DeliveryFeedback Feedback, Instant OccurredAt);
