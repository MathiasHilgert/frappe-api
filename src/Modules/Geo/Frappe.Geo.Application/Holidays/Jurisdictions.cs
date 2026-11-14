using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Domain.Territory;
using Frappe.Results;

namespace Frappe.Geo.Application.Holidays;

/// <summary>Resolves the code a route carries to a known country (<c>AR</c>) or subdivision (<c>AR-X</c>).</summary>
internal static class Jurisdictions
{
    public static async Task<Result<Jurisdiction>> ResolveAsync(string code, ICountryCatalog countries, ITerritoryDirectory territory, CancellationToken cancellationToken)
    {
        if (code.Contains('-', StringComparison.Ordinal))
        {
            var subdivision = SubdivisionCode.Create(code);
            if (!subdivision.IsSuccess)
            {
                return Result.Failure<Jurisdiction>(subdivision.Errors);
            }

            return await territory.FindSubdivisionAsync(subdivision.Value, cancellationToken) is null
                ? GeoErrors.SubdivisionNotFound(subdivision.Value.Value)
                : Jurisdiction.Of(subdivision.Value);
        }

        var country = CountryCode.Create(code);
        if (!country.IsSuccess)
        {
            return Result.Failure<Jurisdiction>(country.Errors);
        }

        return await countries.FindAsync(country.Value, cancellationToken) is null
            ? GeoErrors.CountryNotFound(country.Value.Value)
            : Jurisdiction.Of(country.Value);
    }
}
