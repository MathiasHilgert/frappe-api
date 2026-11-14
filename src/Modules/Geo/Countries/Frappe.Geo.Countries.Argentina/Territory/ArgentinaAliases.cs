namespace Frappe.Geo.Countries.Argentina.Territory;

/// <summary>
/// Other names people use for Argentina's subdivisions, curated here because Georef publishes none. Matching is
/// accent- and case-insensitive, so only genuinely different names are listed.
/// </summary>
internal static class ArgentinaAliases
{
    public static readonly IReadOnlyDictionary<string, string[]> ByCode = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["AR-C"] = ["CABA", "Capital Federal", "Ciudad de Buenos Aires"],
        ["AR-B"] = ["Provincia de Buenos Aires", "PBA"],
        ["AR-V"] = ["Tierra del Fuego, Antártida e Islas del Atlántico Sur"],
    };

    public static IReadOnlyList<string> Of(string code) => ByCode.TryGetValue(code, out var aliases) ? aliases : [];
}
