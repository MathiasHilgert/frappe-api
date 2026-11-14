namespace Frappe.Geo.Domain;

/// <summary>Every stable error code Geo answers with, written once. Its texts live in Frappe.Geo.Http's messages.</summary>
public static class GeoErrorCodes
{
    public const string CountryCodeInvalidFormat = "geo.country_code.invalid_format";
    public const string CountryNotFound = "geo.country.not_found";
    public const string CurrencyCodeInvalidFormat = "geo.currency_code.invalid_format";
    public const string CurrencyNotFound = "geo.currency.not_found";
    public const string SubdivisionCodeInvalidFormat = "geo.subdivision_code.invalid_format";
    public const string SubdivisionNotFound = "geo.subdivision.not_found";
    public const string AreaNotFound = "geo.area.not_found";
    public const string AreaLevelOutOfRange = "geo.area_level.out_of_range";
    public const string AreaLevelInvalid = "geo.area_level.invalid";
    public const string LocalityNotFound = "geo.locality.not_found";
    public const string TerritoryNotCovered = "geo.territory.not_covered";
    public const string PageLimitOutOfRange = "geo.page_limit.out_of_range";
    public const string PageLimitInvalid = "geo.page_limit.invalid";
    public const string PageCursorInvalid = "geo.page_cursor.invalid";
}
