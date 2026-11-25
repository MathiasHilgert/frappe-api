using Frappe.Notifications.Application.Channels;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The port that takes a rendered email to a provider (DES-03). The Resend and logging adapters arrive with the dispatch consumer; the
/// channel only renders. A transient fault (429, 5xx, timeout) throws so the message is redelivered; a refusal for good is an outcome.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends <paramref name="email"/>.</summary>
    Task<ChannelOutcome> SendAsync(RenderedEmail email, CancellationToken cancellationToken);
}
