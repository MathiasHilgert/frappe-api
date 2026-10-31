namespace Frappe.Geo.Infrastructure.Currencies;

/// <summary>A row of <c>geo.currency_names</c>: a currency's name in one locale.</summary>
internal sealed class CurrencyNameRow
{
    public string CurrencyCode { get; init; } = null!;

    public string Locale { get; init; } = null!;

    public string Name { get; init; } = null!;
}
