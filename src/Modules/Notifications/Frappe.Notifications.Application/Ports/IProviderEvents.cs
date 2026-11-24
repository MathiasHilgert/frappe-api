using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.ProviderEvents;

namespace Frappe.Notifications.Application.Ports;

/// <summary>The provider events already handled, which dedupe a redelivered webhook and hold the ones parked for a delivery to come.</summary>
public interface IProviderEvents
{
    /// <summary>Whether the provider's event was already recorded.</summary>
    Task<bool> ExistsAsync(DeliveryProvider provider, string eventId, CancellationToken cancellationToken);

    /// <summary>Stages the record; the unit of work saves it.</summary>
    void Stage(ProviderEvent providerEvent);
}
