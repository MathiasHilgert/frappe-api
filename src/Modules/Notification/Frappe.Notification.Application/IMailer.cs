using Frappe.Notification.Domain;

namespace Frappe.Notification.Application;

/// <summary>Sends mail through a provider. Throwing makes the durable queue retry the message.</summary>
public interface IMailer
{
    Task SendAsync(Mail mail, CancellationToken ct);
}
