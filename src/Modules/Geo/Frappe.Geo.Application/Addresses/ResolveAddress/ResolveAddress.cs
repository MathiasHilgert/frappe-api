using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Addresses.ResolveAddress;

/// <summary>
/// The validated, normalized address of a suggestion or of a reference kept with a snapshot (S11), closing the search
/// session; in <paramref name="Language"/>, else <paramref name="RequestLanguage"/>. Values arrive as the client wrote them.
/// </summary>
public sealed record ResolveAddress(string Reference, string? Session, string? Language, LanguageTag RequestLanguage) : IQuery<AddressSnapshot>;
