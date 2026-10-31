using NodaTime.TimeZones;

namespace Frappe.Geo.Domain.Timezones;

/// <summary>
/// An IANA time zone as the catalogue lists it: a canonical zone, or an alias that names the canonical zone it
/// links to. Reference data the migrator seeds; no user changes it.
/// </summary>
public sealed class Timezone
{
    private Timezone(string name, string? canonicalName)
    {
        Name = name;
        CanonicalName = canonicalName;
    }

    /// <summary>Gets the IANA id, such as <c>America/Argentina/Cordoba</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the canonical zone an alias links to, or <see langword="null"/> for a canonical zone.</summary>
    public string? CanonicalName { get; }

    /// <summary>Lists every id of <paramref name="source"/>, each canonical zone with no canonical name.</summary>
    /// <param name="source">The TZDB release, such as the one NodaTime ships.</param>
    /// <returns>One zone per id.</returns>
    public static IReadOnlyList<Timezone> FromTzdb(TzdbDateTimeZoneSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // The map sends every id to its canonical id, so a canonical zone maps to itself.
        return
        [
            .. source.CanonicalIdMap.Select(static pair => new Timezone(
                pair.Key,
                string.Equals(pair.Key, pair.Value, StringComparison.Ordinal) ? null : pair.Value
            )),
        ];
    }
}
