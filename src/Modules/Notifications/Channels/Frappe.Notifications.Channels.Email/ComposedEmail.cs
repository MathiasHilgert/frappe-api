using Frappe.Localization;

namespace Frappe.Notifications.Channels.Email;

/// <summary>A kind's draft with the language its texts were served in. It prints no content (S13).</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Language">The language served.</param>
public sealed record ComposedEmail(EmailDraft Draft, LanguageTag Language)
{
    public override string ToString() => $"ComposedEmail {{ Language = {Language.Value}, Draft = {Draft} }}";
}
