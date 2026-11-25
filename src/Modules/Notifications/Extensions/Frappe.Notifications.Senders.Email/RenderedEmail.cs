using Frappe.Localization;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// An email ready to send: who gets it, what it says in both formats and the headers the provider must add. It is what the sending port
/// receives; it prints no content (S13).
/// </summary>
/// <param name="To">The recipient's normalized address.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Html">The HTML body.</param>
/// <param name="Text">The plain text body.</param>
/// <param name="Headers">Extra headers (<c>List-Unsubscribe</c> and its one-click companion for Marketing).</param>
/// <param name="IdempotencyKey">The provider idempotency key: the same for every attempt of this delivery (S14).</param>
/// <param name="Language">The language it was rendered in.</param>
public sealed record RenderedEmail(
    EmailAddress To,
    string Subject,
    string Html,
    string Text,
    IReadOnlyDictionary<string, string> Headers,
    string IdempotencyKey,
    LanguageTag Language)
{
    public override string ToString() => $"RenderedEmail {{ Language = {Language.Value}, Content = *** }}";
}
