namespace Frappe.Geo.Infrastructure.Countries;

/// <summary>A row of <c>geo.country_currencies</c>: a currency the country uses today, <see cref="Rank"/> 1 first.</summary>
internal sealed class CountryCurrencyRow
{
    public required string CurrencyCode { get; init; }

    public required int Rank { get; set; }
}
