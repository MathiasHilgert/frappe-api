using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Ports;

/// <summary>What a verified unsubscribe token asks for.</summary>
/// <param name="Recipient">Who is unsubscribing, under the key their mail was sent to.</param>
/// <param name="Category">What they turn off.</param>
public sealed record UnsubscribeClaim(RecipientKey Recipient, NotificationCategory Category);
