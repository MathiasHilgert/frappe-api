using Frappe.Geo.Application.Countries;

namespace Frappe.Geo.Application.Catalog;

/// <summary>A country: ISO 3166-1 codes, names, and its currencies in use in order of preference.</summary>
public sealed record CountryImport(string Code, string Alpha3, string Numeric, IReadOnlyList<CountryName> Names, IReadOnlyList<string> Currencies);
