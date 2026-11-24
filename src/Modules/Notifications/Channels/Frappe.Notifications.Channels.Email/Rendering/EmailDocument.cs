using Frappe.Localization;

namespace Frappe.Notifications.Channels.Email.Rendering;

/// <summary>
/// Everything a renderer needs to lay out one email: the draft's content, the language it is written in and, for a Marketing mail, the
/// visible unsubscribe footer the channel adds. Both renderers read it, so the HTML and the text say the same. It prints no content (S13).
/// </summary>
/// <param name="Language">The language of the texts, for the <c>lang</c> attribute.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Preheader">The preheader; empty when none.</param>
/// <param name="Blocks">The blocks, in order.</param>
/// <param name="Unsubscribe">The unsubscribe footer; <see langword="null"/> unless the category can be turned off.</param>
public sealed record EmailDocument(
    LanguageTag Language, string Subject, string Preheader, IReadOnlyList<EmailBlock> Blocks, UnsubscribeFooter? Unsubscribe)
{
    public override string ToString() => $"EmailDocument {{ Language = {Language.Value}, Blocks = {Blocks.Count}, Content = *** }}";
}
