using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// The scopes a country has, as its package declares them (<see cref="Domain.BusinessDays.CalendarRules.Scopes"/>): the
/// general calendar always, and the sector calendars its policies read. Any other scope is refused as invalid, never
/// answered "not covered", since no calendar of it can ever exist.
/// </summary>
internal static class CalendarScopes
{
    /// <summary>The scope <paramref name="raw"/> names in the country, the general one when absent.</summary>
    public static Result<CalendarScope> Resolve(string? raw, Jurisdiction place, ICalendarRulesCatalog rules)
    {
        if (raw is null)
        {
            return CalendarScope.General;
        }

        var country = CountryCode.Create(place.CountryCode).Value;
        IReadOnlyList<CalendarScope> known = rules.Find(country)?.Scopes ?? [CalendarScope.General];
        return CalendarScope.Create(raw) is { IsSuccess: true } scope && known.Contains(scope.Value)
            ? scope.Value
            : GeoErrors.CalendarScopeInvalid(raw, place.CountryCode, known.Select(s => s.Value));
    }
}
