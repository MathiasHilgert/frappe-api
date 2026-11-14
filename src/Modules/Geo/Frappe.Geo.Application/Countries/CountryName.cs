using Frappe.Localization;

namespace Frappe.Geo.Application.Countries;

/// <summary>A country's name in one language.</summary>
public sealed record CountryName(LanguageTag Language, string Text);
