using Frappe.Localization;

namespace Frappe.Http;

/// <summary>
/// The language the client explicitly asked for. Set once per request by the language negotiation
/// middleware; when it is absent nothing is rendered as human text.
/// </summary>
public sealed record RequestLanguage(LanguageTag Tag);
