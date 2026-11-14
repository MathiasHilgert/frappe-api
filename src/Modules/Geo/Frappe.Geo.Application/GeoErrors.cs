using Frappe.Geo.Domain;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;

namespace Frappe.Geo.Application;

/// <summary>The errors Geo's use cases raise themselves; value objects raise their own format errors.</summary>
internal static class GeoErrors
{
    public static NotFoundError CountryNotFound(string countryCode) => new(GeoErrorCodes.CountryNotFound, ("countryCode", countryCode));

    public static NotFoundError CurrencyNotFound(string currencyCode) => new(GeoErrorCodes.CurrencyNotFound, ("currencyCode", currencyCode));

    public static NotFoundError SubdivisionNotFound(string subdivisionCode) => new(GeoErrorCodes.SubdivisionNotFound, ("subdivisionCode", subdivisionCode));

    public static NotFoundError AreaNotFound(string? areaId) => new(GeoErrorCodes.AreaNotFound, ("areaId", areaId));

    public static NotFoundError LocalityNotFound(string? localityId) => new(GeoErrorCodes.LocalityNotFound, ("localityId", localityId));

    /// <summary>Geo has no calendar of <paramref name="jurisdiction"/> (the one actually missing) for that year and scope.</summary>
    public static NotFoundError HolidaysNotCovered(string jurisdiction, int year, string scope) =>
        new(GeoErrorCodes.HolidaysNotCovered, ("jurisdiction", jurisdiction), ("year", year), ("scope", scope));

    /// <summary>Geo holds no subdivisions of a known country: no data, never "no subdivisions" (as holidays, S6).</summary>
    public static NotFoundError TerritoryNotCovered(string countryCode) => new(GeoErrorCodes.TerritoryNotCovered, ("countryCode", countryCode));

    /// <summary>A required parameter (query, route or body field) is absent or blank.</summary>
    public static InvalidValueError ParameterRequired(string parameter) => Error.InvalidValue(GeoErrorCodes.ParameterRequired, ("parameter", parameter));

    /// <summary>The value, or <see cref="ParameterRequired"/> when it is absent or blank.</summary>
    public static Result<string> Required(string? raw, string parameter) =>
        string.IsNullOrWhiteSpace(raw) ? ParameterRequired(parameter) : raw;

    /// <summary>
    /// A scope the country does not declare (<paramref name="scopes"/> are those it does): unknown and malformed alike,
    /// since only a declared one has a calendar.
    /// </summary>
    public static InvalidValueError CalendarScopeInvalid(string? value, string countryCode, IEnumerable<string> scopes) =>
        Error.InvalidValue(GeoErrorCodes.CalendarScopeInvalid, ("value", value), ("max", CalendarScope.MaxLength), ("countryCode", countryCode), ("scopes", scopes.ToArray()));

    public static NotFoundError HolidayOverrideNotFound(string jurisdiction, string date, string name) =>
        new(GeoErrorCodes.HolidayOverrideNotFound, ("jurisdiction", jurisdiction), ("date", date), ("name", name));

    public static NotFoundError HolidaySyncNotFound(string sourceId) => new(GeoErrorCodes.HolidaySyncNotFound, ("sourceId", sourceId));

    public static InvalidValueError AreaLevelOutOfRange(string? value) =>
        Error.InvalidValue(GeoErrorCodes.AreaLevelOutOfRange, ("value", value), ("min", 2), ("max", 9));

    public static InvalidValueError AreaLevelInvalid(string? value) =>
        Error.InvalidValue(GeoErrorCodes.AreaLevelInvalid, ("value", value), ("min", 2), ("max", 9));

    public static InvalidValueError PageLimitOutOfRange(string? value, int max) =>
        Error.InvalidValue(GeoErrorCodes.PageLimitOutOfRange, ("value", value), ("min", 1), ("max", max));

    public static InvalidValueError PageLimitInvalid(string? value, int max) =>
        Error.InvalidValue(GeoErrorCodes.PageLimitInvalid, ("value", value), ("min", 1), ("max", max));

    public static InvalidValueError PageCursorInvalid() => Error.InvalidValue(GeoErrorCodes.PageCursorInvalid);
}
