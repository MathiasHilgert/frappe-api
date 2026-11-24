using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>The key both sides of the message lock hash, so they contend for the same Postgres advisory lock.</summary>
internal static class ProviderMessageLock
{
    internal static string KeyOf(DeliveryProvider provider, string providerMessageId) => $"notifications:{provider}:{providerMessageId}";
}
