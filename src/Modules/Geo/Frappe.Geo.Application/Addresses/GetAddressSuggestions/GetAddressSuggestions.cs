using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Addresses.GetAddressSuggestions;

/// <summary>
/// Addresses that match what the user typed so far (S11), in a search session, optionally within one country and in one
/// language (else <paramref name="RequestLanguage"/>, the negotiated one). Every value arrives as the client wrote it.
/// </summary>
public sealed record GetAddressSuggestions(string? Query, string? Session, string? Country, string? Language, LanguageTag RequestLanguage)
    : IQuery<IReadOnlyList<AddressSuggestion>>;
