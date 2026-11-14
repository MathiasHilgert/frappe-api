// Prints Geo's CLDR reference data (countries, currencies and their names) as the JSON snapshot Frappe.Geo.Global embeds.
//
// Source: CLDR 49.0.0 in JSON, released 2026-10-22,
//   https://github.com/unicode-org/cldr-json/releases/download/49.0.0/cldr-49.0.0-json-full.zip
//   (SHA-256 3b1f5d684d29c541d3acf91fe834b3fe36ae0f34775e3ee50e93558bf14ca174), Unicode License v3.
// Usage (from the repository root; the archive stays outside the repository):
//   curl -sSLo /tmp/cldr.zip <url above> && unzip -q /tmp/cldr.zip -d /tmp/cldr
//   dotnet run tools/seeds/cldr-seed.cs -- /tmp/cldr > src/Modules/Geo/Frappe.Geo.Global/Cldr/cldr-49.0.0.json
// A CLDR update replaces the file and CldrCatalogSeed.Version; the Migrator applies the difference.
//
// What it selects:
// - countries: the ISO 3166-1 alpha-2 codes, i.e. CLDR territories with a two-letter code, an alpha-3 code and an ISO
//   numeric code below 900 (codeMappings.json). That leaves out regions (001, 419), user-assigned codes (QO, XA, XK,
//   ZZ) and the EU, and gives exactly the 249 ISO countries;
// - a country's currencies: its currencyData.json entries in use today (no "_to") that are legal tender (not
//   "_tender": "false"), in CLDR's order, which is the order of preference;
// - currencies: every currency some country uses today as legal tender, with its minor unit digits (currencyData.json
//   "fractions", CLDR's DEFAULT of 2 when absent);
// - names: the country's name (territories.json) and the currency's name and standard symbol (currencies.json) in each
//   platform language, resolved through CLDR's inheritance (es-AR → es-419 → es). A regional row (es-AR, es-MX) is
//   written only where it differs from the common language, since the platform falls back to it anyway. CLDR's "pt"
//   is Brazilian Portuguese, the platform's pt-BR.
// The output is ordered by code and language (ordinal), so the same release always prints the same text.

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frappe.Tools.Seeds;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    await Console.Error.WriteLineAsync("Usage: dotnet run tools/seeds/cldr-seed.cs -- <directory where cldr-" + Cldr.Version + "-json-full.zip was extracted>");
    return 2;
}

var cldr = new Cldr(args[0]);
if (cldr.PackageVersion() is var version && version != Cldr.Version)
{
    await Console.Error.WriteLineAsync($"Expected CLDR {Cldr.Version}, found {version}.");
    return 1;
}

var seed = new JsonObject { ["cldrVersion"] = Cldr.Version, ["countries"] = cldr.Countries(), ["currencies"] = cldr.Currencies() };
await Console.Out.WriteLineAsync(seed.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
return 0;

namespace Frappe.Tools.Seeds
{
    internal sealed class Cldr(string root)
    {
        public const string Version = "49.0.0";

        private static readonly (string Platform, string Cldr)[] Languages =
            [("en", "en"), ("es", "es"), ("es-AR", "es-AR"), ("es-MX", "es-MX"), ("pt-BR", "pt")];

        private JsonObject? _parents;
        private JsonObject? _codeMappings;
        private JsonNode? _currencyData;

        private JsonObject Parents => _parents ??= Load("cldr-core", "supplemental", "parentLocales.json")["supplemental"]!["parentLocales"]!["parentLocale"]!.AsObject();

        private JsonObject CodeMappings => _codeMappings ??= Load("cldr-core", "supplemental", "codeMappings.json")["supplemental"]!["codeMappings"]!.AsObject();

        private JsonNode CurrencyData => _currencyData ??= Load("cldr-core", "supplemental", "currencyData.json")["supplemental"]!["currencyData"]!;

        public string PackageVersion() => Load("cldr-core", "package.json")["version"]!.GetValue<string>();

        public JsonArray Countries()
        {
            var countries = new JsonArray();
            foreach (var code in CountryCodes())
            {
                var mapping = CodeMappings[code]!;
                countries.Add((JsonNode)new JsonObject
                {
                    ["code"] = code,
                    ["alpha3"] = mapping["_alpha3"]!.GetValue<string>(),
                    ["numeric"] = mapping["_numeric"]!.GetValue<string>(),
                    ["names"] = Translations(locale => Resolve(locale, "cldr-localenames-full", "territories.json",
                        main => main["localeDisplayNames"]!["territories"]![code])),
                    ["currencies"] = new JsonArray([.. CurrentCurrencies(code).Select(c => (JsonNode)JsonValue.Create(c))]),
                });
            }

            return countries;
        }

        public JsonArray Currencies()
        {
            var fractions = CurrencyData["fractions"]!.AsObject();
            var currencies = new JsonArray();
            foreach (var code in CountryCodes().SelectMany(CurrentCurrencies).Distinct().Order(StringComparer.Ordinal))
            {
                var digits = fractions[code]?["_digits"] ?? fractions["DEFAULT"]!["_digits"]!;
                currencies.Add((JsonNode)new JsonObject
                {
                    ["code"] = code,
                    ["minorUnitDigits"] = int.Parse(digits.GetValue<string>(), CultureInfo.InvariantCulture),
                    ["names"] = Translations(locale => CurrencyName(locale, code)),
                });
            }

            return currencies;
        }

        /// <summary>The ISO 3166-1 countries: two letters, an alpha-3 code and an ISO numeric code below 900.</summary>
        private string[] CountryCodes() =>
            Load("cldr-localenames-full", "main", "en", "territories.json")["main"]!["en"]!["localeDisplayNames"]!["territories"]!.AsObject()
                .Select(t => t.Key)
                .Where(IsCountry)
                .Order(StringComparer.Ordinal)
                .ToArray();

        private bool IsCountry(string code) =>
            code.Length == 2 && code.All(char.IsAsciiLetterUpper)
            && CodeMappings[code] is JsonObject mapping
            && mapping["_alpha3"] is not null
            && int.TryParse(mapping["_numeric"]?.GetValue<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out var numeric)
            && numeric < 900;

        /// <summary>The country's currencies in use today as legal tender, in CLDR's order of preference.</summary>
        private string[] CurrentCurrencies(string country) => CurrencyData["region"]![country] is JsonArray entries
            ? entries
                .Select(entry => entry!.AsObject().Single())
                .Where(entry => entry.Value!["_to"] is null && entry.Value["_tender"]?.GetValue<string>() != "false")
                .Select(entry => entry.Key)
                .ToArray()
            : [];

        private JsonObject? CurrencyName(string locale, string code)
        {
            var name = Resolve(locale, "cldr-numbers-full", "currencies.json", main => main["numbers"]!["currencies"]![code]?["displayName"]);
            if (name is null)
            {
                return null;
            }

            // CLDR's rule when a locale has no symbol: the ISO code itself.
            var symbol = Resolve(locale, "cldr-numbers-full", "currencies.json", main => main["numbers"]!["currencies"]![code]?["symbol"]);
            return new JsonObject { ["name"] = name.GetValue<string>(), ["symbol"] = symbol?.GetValue<string>() ?? code };
        }

        /// <summary>Every platform language that has a value, regional ones only where they differ from their common language.</summary>
        private static JsonObject Translations(Func<string, JsonNode?> resolve)
        {
            var byLanguage = Languages
                .Select(l => (l.Platform, Value: resolve(l.Cldr)))
                .Where(r => r.Value is not null)
                .ToDictionary(r => r.Platform, r => r.Value!.ToJsonString());
            var kept = new JsonObject();
            foreach (var (language, json) in byLanguage.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var common = language.Contains('-') ? language[..language.IndexOf('-')] : null;
                if (common is null || !byLanguage.TryGetValue(common, out var commonJson) || commonJson != json)
                {
                    kept[language] = JsonNode.Parse(json);
                }
            }

            return kept;
        }

        /// <summary>The locale itself, then its parents up to (not including) root.</summary>
        private IEnumerable<string> Chain(string locale)
        {
            for (var current = locale; current is not null && current != "root";)
            {
                yield return current;
                current = Parents[current]?.GetValue<string>() ?? (current.Contains('-') ? current[..current.LastIndexOf('-')] : null);
            }
        }

        private JsonNode? Resolve(string locale, string package, string file, Func<JsonNode, JsonNode?> select) =>
            Chain(locale)
                .Select(candidate => (candidate, path: Path.Combine(root, package, "main", candidate, file)))
                .Where(c => File.Exists(c.path))
                .Select(c => select(JsonNode.Parse(File.ReadAllText(c.path))!["main"]![c.candidate]!))
                .FirstOrDefault(value => value is not null);

        private JsonNode Load(params string[] path) => JsonNode.Parse(File.ReadAllText(Path.Combine([root, .. path])))!;
    }
}
