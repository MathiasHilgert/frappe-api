using Frappe.Notifications.Application.Messages;

namespace Frappe.Notifications.Senders.Email;

// Static abstract members of INotification declare a kind's contract: one class, one kind, so S2743 does not apply.
#pragma warning disable S2743
/// <summary>
/// A notification kind that can be sent by email. It adds one method to <see cref="INotification{TPayload}"/>, so the payload type is
/// declared once. A kind of the Security category returns <see cref="SecureEmail"/> from <see cref="Email.Secure"/>; the channel refuses
/// at startup a Security kind whose sample draft is not one, and fails for good a send whose draft is not.
/// </summary>
/// <typeparam name="TPayload">The data the notification needs.</typeparam>
public interface IEmailNotification<TPayload> : INotification<TPayload>
    where TPayload : notnull
{
    /// <summary>Composes the email from the payload, with its texts already in the language to serve.</summary>
    EmailDraft ToEmail(TPayload payload, NotificationTexts texts);
}
