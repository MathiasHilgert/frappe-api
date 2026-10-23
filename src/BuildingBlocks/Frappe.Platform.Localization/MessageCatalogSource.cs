using System.Reflection;
using System.Text.Json;
using Frappe.SharedKernel.Localization;

namespace Frappe.Platform.Localization;

/// <summary>
/// An assembly whose embedded resources named <c>*.i18n.&lt;locale&gt;.json</c> hold flat <c>{ "key": "pattern" }</c>
/// catalogues, one file per locale. Keys are global, so prefix them with the module name.
/// </summary>
public sealed record MessageCatalogSource(Assembly Assembly)
{
    private const string Marker = ".i18n.";
    private const string Extension = ".json";

    public IReadOnlyDictionary<Locale, IReadOnlyDictionary<string, string>> Load()
    {
        var catalogues = new Dictionary<Locale, IReadOnlyDictionary<string, string>>();
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            var start = name.LastIndexOf(Marker, StringComparison.Ordinal);
            if (start < 0 || !name.EndsWith(Extension, StringComparison.Ordinal))
                continue;

            var locale = Locale.Parse(name[(start + Marker.Length)..^Extension.Length]);
            using var stream = Assembly.GetManifestResourceStream(name)!;
            catalogues[locale] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidDataException($"Catalogue '{name}' is empty.");
        }

        return catalogues;
    }
}
