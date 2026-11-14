using System.Text.Json;
using Frappe.Geo.Application.Catalog;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Currencies;
using Frappe.Localization;

namespace Frappe.Geo.Global.Cldr;

/// <summary>
/// The catalog from Unicode CLDR (Unicode License v3): the embedded snapshot <c>Cldr/cldr-{version}.json</c> that
/// <c>tools/seeds/cldr-seed.cs</c> generates, with the 249 ISO 3166-1 countries and the currencies in use. A CLDR
/// update replaces the file and <see cref="Version"/>.
/// </summary>
public sealed class CldrCatalogSeed : ICatalogSeed
{
    /// <summary>The CLDR release the embedded snapshot comes from.</summary>
    public const string Version = "49.0.0";

    private const string Resource = "frappe.geo.cldr/cldr-" + Version + ".json";

    public CatalogSnapshot Load()
    {
        using var stream = typeof(CldrCatalogSeed).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The CLDR snapshot {Resource} is not embedded.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new CatalogSnapshot(
            [.. root.GetProperty("countries").EnumerateArray().Select(ReadCountry)],
            [.. root.GetProperty("currencies").EnumerateArray().Select(ReadCurrency)]);
    }

    private static CountryImport ReadCountry(JsonElement country) => new(
        country.GetProperty("code").GetString()!,
        country.GetProperty("alpha3").GetString()!,
        country.GetProperty("numeric").GetString()!,
        [.. country.GetProperty("names").EnumerateObject().Select(name => new CountryName(Tag(name.Name), name.Value.GetString()!))],
        [.. country.GetProperty("currencies").EnumerateArray().Select(code => code.GetString()!)]);

    private static CurrencyImport ReadCurrency(JsonElement currency) => new(
        currency.GetProperty("code").GetString()!,
        currency.GetProperty("minorUnitDigits").GetInt32(),
        [.. currency.GetProperty("names").EnumerateObject().Select(name => new CurrencyName(
            Tag(name.Name),
            name.Value.GetProperty("name").GetString()!,
            name.Value.GetProperty("symbol").GetString()!))]);

    private static LanguageTag Tag(string value) => LanguageTag.Create(value).Value;
}
