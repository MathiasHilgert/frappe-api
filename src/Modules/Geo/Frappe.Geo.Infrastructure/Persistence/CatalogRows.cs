using Frappe.Geo.Application.Catalog;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Currencies;

namespace Frappe.Geo.Infrastructure.Persistence;

/// <summary>A catalog snapshot as the rows of <c>geo.countries</c> and <c>geo.currencies</c>.</summary>
internal static class CatalogRows
{
    public static CountryRow[] Countries(CatalogSnapshot snapshot) =>
        [.. snapshot.Countries.Select(c => new CountryRow
        {
            Code = c.Code,
            Alpha3 = c.Alpha3,
            Numeric = c.Numeric,
            Names = [.. c.Names.Select(n => new CountryNameRow { Language = n.Language, Text = n.Text })],
            Currencies = [.. c.Currencies.Select((code, index) => new CountryCurrencyRow { CurrencyCode = code, Rank = index + 1 })],
        })];

    public static CurrencyRow[] Currencies(CatalogSnapshot snapshot) =>
        [.. snapshot.Currencies.Select(c => new CurrencyRow
        {
            Code = c.Code,
            MinorUnitDigits = c.MinorUnitDigits,
            Names = [.. c.Names.Select(n => new CurrencyNameRow { Language = n.Language, Name = n.Name, Symbol = n.Symbol })],
        })];
}
