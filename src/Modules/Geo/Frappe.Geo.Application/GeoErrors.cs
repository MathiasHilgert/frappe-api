using Frappe.Geo.Domain;
using Frappe.Results;

namespace Frappe.Geo.Application;

/// <summary>The errors Geo's use cases raise themselves; value objects raise their own format errors.</summary>
internal static class GeoErrors
{
    public static NotFoundError CountryNotFound(string countryCode) => new(GeoErrorCodes.CountryNotFound, ("countryCode", countryCode));

    public static NotFoundError CurrencyNotFound(string currencyCode) => new(GeoErrorCodes.CurrencyNotFound, ("currencyCode", currencyCode));
}
