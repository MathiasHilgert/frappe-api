using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Infrastructure.Catalogues;

// A subdivision of another country is as unknown to a country's filters as one never catalogued.
internal static class SubdivisionFilter
{
    public static bool BelongsTo(
        this ISubdivisionReader subdivisions,
        SubdivisionCode subdivision,
        CountryCode country
    ) =>
        subdivision.Country == country
        && subdivisions.Find(subdivision, Locale.English) is not null;
}
