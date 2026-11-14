namespace Frappe.Geo.Http.Currencies;

/// <summary>A list of currencies.</summary>
public sealed record CurrencyListResponse(IReadOnlyList<CurrencyResponse> Currencies);
