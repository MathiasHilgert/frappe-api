using Frappe.Localization;

namespace Frappe.Geo.Infrastructure.Currencies;

/// <summary>A row of <c>geo.currency_names</c>: a currency's name and standard symbol in one language.</summary>
internal sealed class CurrencyNameRow
{
    public required LanguageTag Language { get; init; }

    public required string Name { get; set; }

    public required string Symbol { get; set; }
}
