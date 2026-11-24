using Frappe.Aggregates;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// A sent delivery was reported delivered. It carries the time from <c>sent_at</c> to <c>delivered_at</c> for the histogram counted once the
/// commit lands (S15); it holds no address and no payload (S13).
/// </summary>
/// <param name="Kind">The notification kind.</param>
/// <param name="Channel">The channel's name.</param>
/// <param name="TimeToDelivered">How long the provider took from the send to the delivery; never negative.</param>
public sealed record DeliveryDelivered(string Kind, string Channel, TimeSpan TimeToDelivered) : IDomainEvent;
