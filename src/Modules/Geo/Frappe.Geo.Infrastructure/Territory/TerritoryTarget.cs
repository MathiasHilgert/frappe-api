using Frappe.Geo.Application.Sync;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Infrastructure.Persistence;
using Frappe.Geo.Infrastructure.ReferenceData;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>Where every country's territory lands: subdivisions, areas and localities of that country only.</summary>
internal sealed class TerritoryTarget(GeoDbContext db, IClock clock) : ISnapshotTarget<TerritorySnapshot>
{
    public string Kind => SyncKinds.Territory;

    public async Task<SnapshotChanges> ApplyAsync(TerritorySnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var now = clock.GetCurrentInstant();
        var country = snapshot.CountryCode;

        var subdivisions = await ReferenceDataApplier.ApplyAsync(db, db.Set<SubdivisionRow>().Where(s => s.CountryCode == country), [.. snapshot.Subdivisions.Select(s => ToRow(country, s))], now, cancellationToken);
        var areas = await ReferenceDataApplier.ApplyAsync(db, db.Set<AdministrativeAreaRow>().Where(a => a.CountryCode == country), [.. snapshot.Areas.Select(a => ToRow(country, a))], now, cancellationToken);
        var localities = await ReferenceDataApplier.ApplyAsync(db, db.Set<LocalityRow>().Where(l => l.CountryCode == country), [.. snapshot.Localities.Select(l => ToRow(country, l))], now, cancellationToken);
        return SnapshotChanges.Of(subdivisions, areas, localities);
    }

    private static SubdivisionRow ToRow(string country, SubdivisionImport s) => new()
    {
        Code = s.Code,
        CountryCode = country,
        SourceId = s.SourceId,
        Name = s.Name,
        Category = s.Category,
        Latitude = s.Centroid?.Latitude,
        Longitude = s.Centroid?.Longitude,
        // One alias per search form, as the unique index wants: of the source's spellings, the ordinally smallest.
        Aliases = [.. s.Aliases.GroupBy(SearchText.Normalize).Select(g => new SubdivisionAliasRow { Alias = g.Min(StringComparer.Ordinal)!, SearchForm = g.Key })],
    };

    private static AdministrativeAreaRow ToRow(string country, AdministrativeAreaImport a) => new()
    {
        CountryCode = country,
        SourceId = a.Id,
        SubdivisionCode = a.SubdivisionCode,
        Level = a.Level,
        Category = a.Category,
        Name = a.Name,
        IsLocalGovernment = a.IsLocalGovernment,
        Latitude = a.Centroid?.Latitude,
        Longitude = a.Centroid?.Longitude,
    };

    private static LocalityRow ToRow(string country, LocalityImport l) => new()
    {
        CountryCode = country,
        SourceId = l.Id,
        SubdivisionCode = l.SubdivisionCode,
        AreaId = l.AreaId,
        LocalGovernmentId = l.LocalGovernmentId,
        Name = l.Name,
        SearchName = SearchText.Normalize(l.Name),
        Category = l.Category,
        Latitude = l.Centroid?.Latitude,
        Longitude = l.Centroid?.Longitude,
    };
}
