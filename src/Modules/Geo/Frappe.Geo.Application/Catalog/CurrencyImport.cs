using Frappe.Geo.Application.Currencies;

namespace Frappe.Geo.Application.Catalog;

/// <summary>A currency in use: ISO 4217 code, minor unit digits, and its name and symbol per language.</summary>
public sealed record CurrencyImport(string Code, int MinorUnitDigits, IReadOnlyList<CurrencyName> Names);
