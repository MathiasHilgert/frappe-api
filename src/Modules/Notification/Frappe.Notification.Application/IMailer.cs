using Frappe.Notification.Domain;

namespace Frappe.Notification.Application;

/// <summary>
/// Sends mail through a provider. A <see cref="MailRejectedException"/> means retrying cannot help, so the message is
/// dead-lettered; a <see cref="MailDeliveryFailedException"/> means the durable queue retries it with backoff.
/// </summary>
public interface IMailer
{
    Task SendAsync(Mail mail, CancellationToken cancellationToken);
}
