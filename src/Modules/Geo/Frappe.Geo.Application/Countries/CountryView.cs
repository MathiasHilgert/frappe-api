namespace Frappe.Geo.Application.Countries;

/// <summary>A country as stored: its ISO 3166-1 codes, its names and its currencies in use, in order of preference.</summary>
public sealed record CountryView(string Code, string Alpha3, string Numeric, IReadOnlyList<CountryName> Names, IReadOnlyList<string> CurrencyCodes);
