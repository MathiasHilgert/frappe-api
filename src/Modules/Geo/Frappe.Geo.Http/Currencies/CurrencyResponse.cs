namespace Frappe.Geo.Http.Currencies;

/// <summary>A currency: ISO 4217 code, CLDR name and standard symbol (same language), and its minor unit digits.</summary>
public sealed record CurrencyResponse(string Code, string Name, string Symbol, int MinorUnitDigits);
