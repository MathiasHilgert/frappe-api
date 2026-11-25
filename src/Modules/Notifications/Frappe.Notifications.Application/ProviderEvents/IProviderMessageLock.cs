using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.ProviderEvents;

/// <summary>
/// Serializes the two writers that can meet on one provider message: the webhook that reports on it and the Worker that records its delivery
/// (Flow 3). The webhook holds the lock from before it looks the delivery up until its commit; the Worker takes the same lock inside the claim
/// that saves the delivery, so one of them always sees the other's rows: a report is never parked after the Worker already read the parked ones.
/// </summary>
public interface IProviderMessageLock
{
    /// <summary>Waits for the lock of the message and holds it until the returned handle is disposed.</summary>
    Task<IAsyncDisposable> AcquireAsync(DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken);
}
