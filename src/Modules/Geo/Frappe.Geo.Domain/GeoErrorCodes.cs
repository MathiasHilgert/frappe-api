namespace Frappe.Geo.Domain;

/// <summary>Every stable error code Geo answers with, written once. Its texts live in Frappe.Geo.Http's messages.</summary>
public static class GeoErrorCodes
{
    public const string CountryCodeInvalidFormat = "geo.country_code.invalid_format";
    public const string CountryNotFound = "geo.country.not_found";
    public const string CurrencyCodeInvalidFormat = "geo.currency_code.invalid_format";
    public const string CurrencyNotFound = "geo.currency.not_found";
}
