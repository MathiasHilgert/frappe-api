using Frappe.Geo.Domain.Countries;
using Frappe.Localization;

namespace Frappe.Geo.Application.Addresses;

/// <summary>What the user typed so far, in a search session, optionally within one country, answered in <paramref name="Language"/>.</summary>
public sealed record AddressSearch(string Input, AddressSession Session, CountryCode? Country, LanguageTag Language);
