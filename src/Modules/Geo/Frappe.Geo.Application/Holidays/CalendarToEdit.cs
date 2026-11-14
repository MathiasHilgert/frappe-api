using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using NodaTime;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// The stored calendar a manual correction applies to: the jurisdiction's own, of the date's year. A correction never
/// opens a calendar: a calendar's existence is the coverage (S6), so opening one by hand would claim data Geo does not
/// have (a subdivision would answer as if its own holidays were known). The scope picks a sector's calendar
/// (<c>banking</c>) the country declares; without one, the general calendar.
/// </summary>
internal sealed class CalendarToEdit(IHolidayCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, ICalendarRulesCatalog rules)
{
    /// <summary>The calendar to correct, and the rules its country declares (none for a country without a package).</summary>
    public async Task<Result<(HolidayCalendar Calendar, CalendarRules? Rules)>> FindAsync(
        string jurisdictionCode, LocalDate date, string? rawScope, CancellationToken cancellationToken)
    {
        var year = HolidayYear.Create(date.Year);
        if (!year.IsSuccess)
        {
            return Result.Failure<(HolidayCalendar, CalendarRules?)>(year.Errors);
        }

        var jurisdiction = await Jurisdictions.ResolveAsync(jurisdictionCode, countries, territory, cancellationToken);
        if (!jurisdiction.IsSuccess)
        {
            return Result.Failure<(HolidayCalendar, CalendarRules?)>(jurisdiction.Errors);
        }

        var place = jurisdiction.Value;
        var scope = CalendarScopes.Resolve(rawScope, place, rules);
        if (!scope.IsSuccess)
        {
            return Result.Failure<(HolidayCalendar, CalendarRules?)>(scope.Errors);
        }

        return await calendars.FindAsync(place, year.Value, scope.Value, cancellationToken) is { } calendar
            ? (calendar, rules.Find(CountryCode.Create(place.CountryCode).Value))
            : GeoErrors.HolidaysNotCovered(place.Value, date.Year, scope.Value.Value);
    }
}
