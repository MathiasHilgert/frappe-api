using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Ports;

/// <summary>Finds the deliveries a provider reports on.</summary>
public interface IDeliveries
{
    /// <summary>The delivery the provider knows by this message id, tracked so the unit of work saves a change to it; none when absent.</summary>
    Task<Delivery?> FindByProviderMessageAsync(DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken);
}
