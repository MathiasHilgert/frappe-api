using Frappe.Notifications.Application.Messages;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The email channel's trial of a payload: composes the kind's email once, so what the kind refuses while writing it (an action that does not
/// go to an absolute https url) is found when the request is accepted. Kinds without an email pass. A kind with no texts or a link in a
/// Security kind is a bug the startup check finds, not a payload fault, so it is not swallowed here.
/// </summary>
public sealed class EmailPayloadCheck(EmailComposer composer) : INotificationPayloadCheck
{
    /// <inheritdoc />
    public bool Accepts(NotificationKind kind, object payload)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        if (!composer.CanCompose(kind))
        {
            return true;
        }

        try
        {
            _ = composer.Compose(kind, payload, requested: null);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
