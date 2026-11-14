namespace Frappe.Geo.Application.Currencies;

/// <summary>A currency as stored: its ISO 4217 code, the digits of its minor unit and its names.</summary>
public sealed record CurrencyView(string Code, int MinorUnitDigits, IReadOnlyList<CurrencyName> Names);
