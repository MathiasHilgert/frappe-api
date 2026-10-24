using Frappe.Notification.Domain;

namespace Frappe.Notification.Application.OwnerCodeMails;

/// <summary>Writes a code mail in the owner's language.</summary>
public interface IOwnerCodeMails
{
    /// <summary>The mail for one code, subject included, in plain text and HTML.</summary>
    /// <param name="kind">Which code the mail carries.</param>
    /// <param name="to">The recipient.</param>
    /// <param name="name">The owner's name, for the greeting.</param>
    /// <param name="code">The one-time code, shown in the subject and the body.</param>
    /// <param name="locale">
    /// The owner's stored locale. Absent, invalid or unsupported, the mail goes out in the platform default: a code
    /// mail is never held back for want of a translation.
    /// </param>
    Mail Compose(OwnerCodeMailKind kind, string to, string name, string code, string? locale);
}
