using Frappe.Localization;

namespace Frappe.Geo.Application.Countries;

/// <summary>A country as served: its name in the language picked for the request (<see cref="NameLanguage"/>).</summary>
public sealed record CountrySummary(string Code, string Alpha3, string Numeric, string Name, LanguageTag NameLanguage, IReadOnlyList<string> CurrencyCodes)
{
    internal static CountrySummary Serve(CountryView country, LanguageTag language)
    {
        // The seed guarantees every country a name; a country without one would be a broken seed, not a client error.
        var name = LanguageFallback.Pick(country.Names, n => n.Language, language)
            ?? throw new InvalidOperationException($"Country {country.Code} has no name.");
        return new(country.Code, country.Alpha3, country.Numeric, name.Text, name.Language, country.CurrencyCodes);
    }
}
