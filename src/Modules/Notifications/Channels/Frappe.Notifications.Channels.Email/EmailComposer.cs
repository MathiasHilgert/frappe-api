using System.Collections.Concurrent;
using Frappe.Localization;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Asks a kind for its draft in the language to serve, and enforces what the category forbids: a Security notification with a link is a
/// bug of the kind, found here (and at startup by <see cref="EmailKindsCheck"/>), not by a recipient.
/// </summary>
public sealed class EmailComposer(IcuNotificationTexts texts)
{
    private readonly ConcurrentDictionary<string, Func<object, NotificationTexts, EmailDraft>?> _drafts = new(StringComparer.Ordinal);

    /// <summary>Whether the kind implements <see cref="IEmailNotification{TPayload}"/> for its payload.</summary>
    public bool CanCompose(NotificationKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return DraftOf(kind) is not null;
    }

    /// <summary>The kind's draft for <paramref name="payload"/>, in <paramref name="requested"/> or the language it falls back to.</summary>
    /// <exception cref="InvalidOperationException">The kind has no email, has no texts, or is Security and carries a link.</exception>
    public ComposedEmail Compose(NotificationKind kind, object payload, LanguageTag? requested)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        var draftOf = DraftOf(kind) ?? throw new InvalidOperationException($"Notification kind '{kind.Name}' cannot be sent by email.");
        var kindTexts = texts.For(kind.Name, requested);
        var draft = draftOf(payload, kindTexts);
        if (draft.HasAction && !kind.Category.AllowsLinks)
        {
            throw new InvalidOperationException($"Notification kind '{kind.Name}' is {kind.Category} and must not carry a link.");
        }

        return new ComposedEmail(draft, kindTexts.Language);
    }

    private Func<object, NotificationTexts, EmailDraft>? DraftOf(NotificationKind kind) =>
        _drafts.GetOrAdd(kind.Name, _ => Bind(kind));

    /// <summary>Closes <see cref="DraftBinding.Bound{TPayload}"/> over the kind's payload type once, so a send is a plain delegate call.</summary>
    private static Func<object, NotificationTexts, EmailDraft>? Bind(NotificationKind kind)
    {
        if (!typeof(IEmailNotification<>).MakeGenericType(kind.PayloadType).IsInstanceOfType(kind.Notification))
        {
            return null;
        }

        var bind = typeof(DraftBinding).GetMethod(nameof(DraftBinding.Bound))!;
        return (Func<object, NotificationTexts, EmailDraft>)bind.MakeGenericMethod(kind.PayloadType).Invoke(null, [kind.Notification])!;
    }
}
