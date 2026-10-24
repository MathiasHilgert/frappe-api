using Frappe.Notification.Domain;

namespace Frappe.Notification.Application.Mails;

/// <summary>
/// Sends mail through a provider. A <see cref="MailRejectedException"/> means retrying cannot help, so the message is
/// dead-lettered; a <see cref="MailDeliveryFailedException"/> means the durable queue retries it with backoff.
/// </summary>
public interface IMailer
{
    /// <summary>Sends <paramref name="mail"/>.</summary>
    /// <param name="mail">What to send.</param>
    /// <param name="idempotencyKey">
    /// Stable across retries of one delivery (see <see cref="MailDelivery.KeyFor"/>): the provider drops a repeat, so a
    /// retry after a timeout never sends the mail twice.
    /// </param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(Mail mail, string idempotencyKey, CancellationToken cancellationToken);
}
