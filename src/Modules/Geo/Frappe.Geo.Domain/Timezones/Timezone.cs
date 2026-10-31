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
}
