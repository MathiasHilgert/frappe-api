using Frappe.Geo.Infrastructure.ReferenceData;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>A row of <c>geo.subdivisions</c>: a first-level subdivision, with its aliases as owned rows.</summary>
internal sealed class SubdivisionRow : IReferenceRow<SubdivisionRow>
{
    public required string Code { get; init; }

    public required string CountryCode { get; init; }

    public required string SourceId { get; set; }

    public required string Name { get; set; }

    public required string Category { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public Instant? RetiredAt { get; set; }

    public List<SubdivisionAliasRow> Aliases { get; init; } = [];

    string IReferenceRow<SubdivisionRow>.Key => Code;

    /// <summary>The source's aliases only: the manual ones are not the source's to compare or replace.</summary>
    public bool SameAs(SubdivisionRow desired) =>
        (SourceId, Name, Category, Latitude, Longitude) == (desired.SourceId, desired.Name, desired.Category, desired.Latitude, desired.Longitude)
        && Imported().Select(a => a.Alias).Order(StringComparer.Ordinal).SequenceEqual(ImportedOf(desired).Select(a => a.Alias).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>
    /// Takes the source's values and replaces the source's aliases, keeping the manual ones; a source alias the
    /// subdivision already has by hand (ignoring case and accents) is skipped, the manual one stays as it is, and the
    /// source's comes back at the next applied sync after the manual one is removed.
    /// </summary>
    public void CopyFrom(SubdivisionRow desired)
    {
        (SourceId, Name, Category, Latitude, Longitude) = (desired.SourceId, desired.Name, desired.Category, desired.Latitude, desired.Longitude);
        var imported = Imported().ToList();
        ReferenceDataApplier.SyncChildren(imported, ImportedOf(desired), a => a.Alias, (stored, wanted) => stored.SearchForm = wanted.SearchForm);
        Aliases.RemoveAll(a => !a.Manual && !imported.Contains(a));
        Aliases.AddRange(imported.Where(a => !Aliases.Contains(a)));
    }

    private IEnumerable<SubdivisionAliasRow> Imported() => Aliases.Where(a => !a.Manual);

    /// <summary>The desired aliases this subdivision does not already have by hand, compared by search form.</summary>
    private IEnumerable<SubdivisionAliasRow> ImportedOf(SubdivisionRow desired) =>
        desired.Aliases.Where(d => !Aliases.Any(a => a.Manual && a.SearchForm == d.SearchForm));
}
