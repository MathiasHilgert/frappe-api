namespace Frappe.Geo.Domain;

/// <summary>Every stable error code Geo answers with, written once. Its texts live in Frappe.Geo.Http's messages.</summary>
public static class GeoErrorCodes
{
    /// <summary>A required parameter (query, route or body) is absent or blank; its name is the <c>parameter</c>.</summary>
    public const string ParameterRequired = "geo.parameter.required";
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
    public const string HolidayYearInvalid = "geo.holiday_year.invalid";
    public const string HolidayYearOutOfRange = "geo.holiday_year.out_of_range";
    public const string CalendarScopeInvalid = "geo.calendar_scope.invalid";
    public const string HolidaysNotCovered = "geo.holidays.not_covered";
    public const string DateInvalidFormat = "geo.date.invalid_format";
    public const string BusinessDayPolicyInvalid = "geo.business_day_policy.invalid";
    public const string BusinessDaysNotCovered = "geo.business_days.not_covered";
    public const string BusinessDayCountOutOfRange = "geo.business_day_count.out_of_range";
    public const string BusinessDayCountInvalid = "geo.business_day_count.invalid";
    public const string ObservanceDateOutsideYear = "geo.observance.date_outside_year";
    public const string ObservanceDuplicate = "geo.observance.duplicate";
    public const string ObservanceInvalid = "geo.observance.invalid";
    public const string ObservanceNotInSource = "geo.observance.not_in_source";
    public const string HolidayOverrideNotFound = "geo.holiday_override.not_found";
    public const string HolidayCalendarChangedConcurrently = "geo.holiday_calendar.changed_concurrently";
    public const string HolidaySyncNotFound = "geo.holiday_sync.not_found";
    public const string SubdivisionAliasInvalid = "geo.subdivision_alias.invalid";
    public const string SubdivisionAliasDuplicate = "geo.subdivision_alias.duplicate";
    public const string SubdivisionAliasNotFound = "geo.subdivision_alias.not_found";
    public const string SubdivisionAliasTaken = "geo.subdivision_alias.taken";
    public const string SubdivisionAliasImported = "geo.subdivision_alias.imported";
    public const string AddressQueryInvalid = "geo.address_query.invalid";
    public const string AddressSessionInvalid = "geo.address_session.invalid";
    public const string AddressReferenceInvalid = "geo.address_reference.invalid";
    public const string AddressNotFound = "geo.address.not_found";
    public const string AddressesUnavailable = "geo.addresses.unavailable";
    public const string AddressesNotConfigured = "geo.addresses.not_configured";
}
