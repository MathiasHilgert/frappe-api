using Frappe.Localization;

namespace Frappe.Geo.Application.Currencies;

/// <summary>A currency as served: name and symbol in the language picked for the request (<see cref="Language"/>).</summary>
public sealed record CurrencySummary(string Code, string Name, string Symbol, LanguageTag Language, int MinorUnitDigits)
{
    internal static CurrencySummary Serve(CurrencyView currency, LanguageTag language)
    {
        // The seed guarantees every currency a name; one without would be a broken seed, not a client error.
        var name = LanguageFallback.Pick(currency.Names, n => n.Language, language)
            ?? throw new InvalidOperationException($"Currency {currency.Code} has no name.");
        return new(currency.Code, name.Name, name.Symbol, name.Language, currency.MinorUnitDigits);
    }
}
