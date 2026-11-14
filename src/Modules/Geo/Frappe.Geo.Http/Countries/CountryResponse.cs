namespace Frappe.Geo.Http.Countries;

/// <summary>A country: ISO 3166-1 alpha-2, alpha-3 and numeric codes, its name, and its currencies in order of preference.</summary>
public sealed record CountryResponse(string Code, string Alpha3, string Numeric, string Name, IReadOnlyList<string> Currencies);
