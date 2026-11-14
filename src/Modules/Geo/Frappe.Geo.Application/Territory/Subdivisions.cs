using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Territory;
using Frappe.Results;

namespace Frappe.Geo.Application.Territory;

/// <summary>Resolves the code a route carries to a known subdivision: a current one, or a retired one too when asked.</summary>
internal static class Subdivisions
{
    public static async Task<Result<SubdivisionCode>> ResolveAsync(
        string code, ICountryCatalog countries, ITerritoryDirectory territory, CancellationToken cancellationToken, bool retiredToo = false)
    {
        var subdivision = SubdivisionCode.Create(code);
        if (!subdivision.IsSuccess)
        {
            return subdivision;
        }

        var known = retiredToo
            ? await territory.HasHeldSubdivisionAsync(subdivision.Value, cancellationToken)
            : await territory.FindSubdivisionAsync(subdivision.Value, cancellationToken) is not null;
        return !known
            ? await MissingAsync(subdivision.Value, countries, territory, cancellationToken)
            : subdivision;
    }

    /// <summary>
    /// Why Geo does not know <paramref name="code"/>: a known country Geo holds no territory of is not covered (as its
    /// list of subdivisions, never "does not exist"); a covered country without it, or a country Geo does not know, has
    /// no such subdivision.
    /// </summary>
    public static async Task<NotFoundError> MissingAsync(
        SubdivisionCode code, ICountryCatalog countries, ITerritoryDirectory territory, CancellationToken cancellationToken)
    {
        var country = CountryCode.Create(code.CountryCode);
        var known = country.IsSuccess && await countries.FindAsync(country.Value, cancellationToken) is not null;
        return known && (await territory.ListSubdivisionsAsync(code.CountryCode, cancellationToken)).Count == 0
            ? GeoErrors.TerritoryNotCovered(code.CountryCode)
            : GeoErrors.SubdivisionNotFound(code.Value);
    }
}
