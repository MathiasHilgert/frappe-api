namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The generic half of <see cref="EmailComposer"/>'s binding: it is public so reflection can close it without bypassing accessibility.
/// </summary>
internal static class DraftBinding
{
    /// <summary>A delegate that calls <c>ToEmail</c> of <paramref name="notification"/> with an untyped payload.</summary>
    public static Func<object, NotificationTexts, EmailDraft> Bound<TPayload>(object notification)
        where TPayload : notnull
    {
        var typed = (IEmailNotification<TPayload>)notification;
        return (payload, texts) => typed.ToEmail((TPayload)payload, texts);
    }
}
