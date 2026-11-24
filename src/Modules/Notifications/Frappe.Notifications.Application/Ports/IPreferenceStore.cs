using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Ports;

/// <summary>Reads what recipients turned off.</summary>
public interface IPreferenceStore
{
    Task<bool> IsOptedOutAsync(RecipientKey recipientKey, NotificationCategory category, CancellationToken cancellationToken);
}
