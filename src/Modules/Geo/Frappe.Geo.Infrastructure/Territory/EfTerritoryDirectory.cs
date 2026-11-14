using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.Territory;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Territory;

internal sealed class EfTerritoryDirectory(GeoDbContext db) : ITerritoryDirectory
{
    /// <summary>How similar a word of a name must be to the search to match (pg_trgm word similarity, 0 to 1).</summary>
    private const double SearchThreshold = 0.5;

    public async Task<IReadOnlyList<SubdivisionView>> ListSubdivisionsAsync(string countryCode, CancellationToken cancellationToken)
    {
        var rows = await db.Set<SubdivisionRow>().AsNoTracking()
            .Where(s => s.CountryCode == countryCode && s.RetiredAt == null)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<SubdivisionView?> FindSubdivisionAsync(SubdivisionCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var row = await db.Set<SubdivisionRow>().AsNoTracking().SingleOrDefaultAsync(s => s.Code == code.Value && s.RetiredAt == null, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public Task<bool> HasHeldSubdivisionAsync(SubdivisionCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        return db.Set<SubdivisionRow>().AnyAsync(s => s.Code == code.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<AdministrativeAreaView>> ListAreasAsync(SubdivisionCode subdivision, int? level, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subdivision);
        var rows = await db.Set<AdministrativeAreaRow>().AsNoTracking()
            .Where(a => a.SubdivisionCode == subdivision.Value && a.RetiredAt == null && (level == null || a.Level == level))
            .OrderBy(a => a.Level).ThenBy(a => a.Name).ThenBy(a => a.SourceId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<AdministrativeAreaView?> FindAreaAsync(string countryCode, string sourceId, CancellationToken cancellationToken)
    {
        var row = await db.Set<AdministrativeAreaRow>().AsNoTracking().SingleOrDefaultAsync(a => a.CountryCode == countryCode && a.SourceId == sourceId, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<LocalityPage> PageLocalitiesAsync(SubdivisionCode subdivision, string? search, int limit, LocalityCursor? after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subdivision);
        var offset = after?.Offset ?? 0;
        var rows = search is null
            ? await db.Set<LocalityRow>().AsNoTracking()
                .Where(l => l.SubdivisionCode == subdivision.Value && l.RetiredAt == null)
                .OrderBy(l => l.SearchName).ThenBy(l => l.SourceId)
                .Skip(offset).Take(limit + 1)
                .ToListAsync(cancellationToken)
            : await SearchAsync(subdivision.Value, SearchText.Normalize(search), offset, limit + 1, cancellationToken);

        var next = rows.Count > limit
            ? new LocalityCursor(offset + limit, LocalityCursor.FingerprintOf(subdivision.Value, search)).Encode()
            : null;
        return new LocalityPage([.. rows.Take(limit).Select(ToView)], next);
    }

    /// <summary>Names containing the search first, then the most similar words (typos tolerated), then by name.</summary>
    private Task<List<LocalityRow>> SearchAsync(string subdivision, string search, int offset, int take, CancellationToken cancellationToken) =>
        db.Set<LocalityRow>()
            .FromSql($"""
                SELECT * FROM geo.localities
                WHERE subdivision_code = {subdivision} AND retired_at IS NULL
                  AND (strpos(search_name, {search}) > 0 OR word_similarity({search}, search_name) >= {SearchThreshold})
                ORDER BY strpos(search_name, {search}) = 0, word_similarity({search}, search_name) DESC, search_name, source_id
                OFFSET {offset} LIMIT {take}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<LocalityView?> FindLocalityAsync(string countryCode, string sourceId, CancellationToken cancellationToken)
    {
        var row = await db.Set<LocalityRow>().AsNoTracking().SingleOrDefaultAsync(l => l.CountryCode == countryCode && l.SourceId == sourceId, cancellationToken);
        return row is null ? null : ToView(row);
    }

    private static Coordinates? CentroidOf(double? latitude, double? longitude) =>
        latitude is { } lat && longitude is { } lon ? new Coordinates(lat, lon) : null;

    private static SubdivisionView ToView(SubdivisionRow row) =>
        new(row.Code, row.CountryCode, row.Name, row.Category, CentroidOf(row.Latitude, row.Longitude), [.. row.Aliases.Select(a => a.Alias).Order(StringComparer.Ordinal)]);

    private static AdministrativeAreaView ToView(AdministrativeAreaRow row) =>
        new(PublicId.Of(row.CountryCode, row.SourceId), row.SubdivisionCode, row.Level, row.Category, row.Name, row.IsLocalGovernment, CentroidOf(row.Latitude, row.Longitude));

    private static LocalityView ToView(LocalityRow row) => new(
        PublicId.Of(row.CountryCode, row.SourceId),
        row.SubdivisionCode,
        row.Name,
        row.Category,
        row.AreaId is null ? null : PublicId.Of(row.CountryCode, row.AreaId),
        row.LocalGovernmentId is null ? null : PublicId.Of(row.CountryCode, row.LocalGovernmentId),
        CentroidOf(row.Latitude, row.Longitude));
}
