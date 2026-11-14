using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Geo.Application.Holidays.GetHolidays;

/// <summary>Reads storage directly (no cache): an override must show in the next answer.</summary>
internal sealed class GetHolidaysHandler(
    IHolidayCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, ICalendarRulesCatalog rules, IClock clock, GeoMetrics metrics)
    : IQueryHandler<GetHolidays, HolidayCalendarView>
{
    public async Task<Result<HolidayCalendarView>> HandleAsync(GetHolidays query, CancellationToken cancellationToken)
    {
        var year = query.Year is null ? HolidayYear.Create(clock.GetCurrentInstant().InUtc().Year) : HolidayYear.Parse(query.Year);
        if (!year.IsSuccess)
        {
            return Result.Failure<HolidayCalendarView>(year.Errors);
        }

        var jurisdiction = await Jurisdictions.ResolveAsync(query.Jurisdiction, countries, territory, cancellationToken);
        if (!jurisdiction.IsSuccess)
        {
            return Result.Failure<HolidayCalendarView>(jurisdiction.Errors);
        }

        var place = jurisdiction.Value;
        var scope = CalendarScopes.Resolve(query.Scope, place, rules);
        if (!scope.IsSuccess)
        {
            return Result.Failure<HolidayCalendarView>(scope.Errors);
        }

        if (await CoveredCalendar.LoadAsync(new StoredEffectiveCalendars(calendars), place, year.Value, scope.Value, cancellationToken) is { } covered)
        {
            return new HolidayCalendarView(place.Value, year.Value.Value, covered.Covers, covered.Attributions, covered.Observances, scope.Value.Value);
        }

        metrics.NotCovered(place.CountryCode, GeoErrorCodes.HolidaysNotCovered);
        return GeoErrors.HolidaysNotCovered(place.Country.Value, year.Value.Value, scope.Value.Value);
    }
}
