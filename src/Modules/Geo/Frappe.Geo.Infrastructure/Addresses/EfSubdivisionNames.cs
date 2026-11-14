using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Infrastructure.Persistence;
using Frappe.Geo.Infrastructure.Territory;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Addresses;

/// <summary>
/// Maps a provider's subdivision names to ISO 3166-2 codes through <c>geo.subdivisions</c>: its name, its aliases (the
/// source's and the manual ones) and its code's own suffix (a provider may write <c>CA</c> for <c>US-CA</c>), all in
/// the search form (<see cref="SearchText"/>), so case and accents never matter. A country has a few dozen
/// subdivisions, so they are compared in memory.
/// </summary>
internal sealed class EfSubdivisionNames(GeoDbContext db) : ISubdivisionNames
{
    public async Task<string?> FindCodeAsync(string countryCode, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);
        var subdivisions = await db.Set<SubdivisionRow>().AsNoTracking()
            .Where(s => s.CountryCode == countryCode && s.RetiredAt == null)
            .ToListAsync(cancellationToken);
        var codesByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var subdivision in subdivisions.OrderBy(s => s.Code, StringComparer.Ordinal))
        {
            string[] known = [SearchText.Normalize(subdivision.Name), SearchText.Normalize(subdivision.Code[(countryCode.Length + 1)..]), .. subdivision.Aliases.Select(a => a.SearchForm)];
            foreach (var name in known)
            {
                codesByName.TryAdd(name, subdivision.Code);
            }
        }

        return names.Select(name => codesByName.GetValueOrDefault(SearchText.Normalize(name))).FirstOrDefault(code => code is not null);
    }
}
