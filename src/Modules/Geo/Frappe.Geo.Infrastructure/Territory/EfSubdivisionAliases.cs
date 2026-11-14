using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.Territory;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>
/// A subdivision's aliases in <c>geo.subdivision_aliases</c>: the manual ones are flagged, and the territory sync
/// (<see cref="SubdivisionRow.CopyFrom"/>) only ever replaces the others. Every comparison is by search form
/// (<see cref="SearchText"/>), as <see cref="Addresses.EfSubdivisionNames"/> maps names to codes.
/// </summary>
internal sealed class EfSubdivisionAliases(GeoDbContext db) : ISubdivisionAliases
{
    public async Task<IReadOnlyList<SubdivisionAliasView>> ListAsync(SubdivisionCode subdivision, CancellationToken cancellationToken)
    {
        var row = await FindAsync(subdivision, cancellationToken);
        return [.. row.Aliases.OrderBy(a => a.Alias, StringComparer.Ordinal).Select(a => new SubdivisionAliasView(a.Alias, a.Manual))];
    }

    /// <summary>
    /// The country's other current subdivisions are read in memory (a country has a few dozen). Two administrators adding
    /// the same alias to two subdivisions at the same instant both pass this check; the database's unique index on the
    /// country's manual aliases (<see cref="SubdivisionAliasRow.OneManualByCountry"/>) lets only the first commit, and
    /// <see cref="GeoDbContext"/> answers the second as taken. An addition racing a territory sync that brings the same
    /// alias to another subdivision, or a sync renaming one, is not guarded: addresses then map it to the smaller code.
    /// </summary>
    public async Task<AliasAddition> TryAddAsync(SubdivisionCode subdivision, string alias, CancellationToken cancellationToken)
    {
        var row = await FindAsync(subdivision, cancellationToken);
        var searchForm = SearchText.Normalize(alias);
        if (KnownNames(row).Contains(searchForm, StringComparer.Ordinal))
        {
            return new AliasAddition(AliasAdditionOutcome.Duplicate);
        }

        var others = await db.Set<SubdivisionRow>().AsNoTracking()
            .Where(s => s.CountryCode == row.CountryCode && s.Code != row.Code && s.RetiredAt == null)
            .ToListAsync(cancellationToken);
        if (others.OrderBy(s => s.Code, StringComparer.Ordinal).FirstOrDefault(s => KnownNames(s).Contains(searchForm, StringComparer.Ordinal)) is { } other)
        {
            return new AliasAddition(AliasAdditionOutcome.Taken, other.Code);
        }

        row.Aliases.Add(new SubdivisionAliasRow { Alias = alias, SearchForm = searchForm, Manual = true });
        return new AliasAddition(AliasAdditionOutcome.Added);
    }

    public async Task<AliasRemoval> TryRemoveAsync(SubdivisionCode subdivision, string alias, CancellationToken cancellationToken)
    {
        var row = await FindAsync(subdivision, cancellationToken);
        var searchForm = SearchText.Normalize(alias);

        // One alias per search form (the unique index), so at most one matches.
        var match = row.Aliases.SingleOrDefault(a => a.SearchForm == searchForm);
        if (match is null)
        {
            return AliasRemoval.NotFound;
        }

        if (!match.Manual)
        {
            return AliasRemoval.Imported;
        }

        row.Aliases.Remove(match);
        return AliasRemoval.Removed;
    }

    /// <summary>Every name a subdivision is known by, in search form: its name, its code's suffix and its aliases.</summary>
    private static IEnumerable<string> KnownNames(SubdivisionRow subdivision) =>
        [SearchText.Normalize(subdivision.Name), SearchText.Normalize(subdivision.Code[(subdivision.CountryCode.Length + 1)..]), .. subdivision.Aliases.Select(a => a.SearchForm)];

    /// <summary>The use case resolved the subdivision first, so it exists.</summary>
    private Task<SubdivisionRow> FindAsync(SubdivisionCode subdivision, CancellationToken cancellationToken) =>
        db.Set<SubdivisionRow>().SingleAsync(s => s.Code == subdivision.Value, cancellationToken);
}
