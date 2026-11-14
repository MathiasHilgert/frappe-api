using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Domain;
using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>
/// Builds a jurisdiction's <see cref="BusinessCalendar"/> of a year under a policy from its country's rules and its
/// effective calendars: the general one, plus the sector calendars the policy names. Each is required at the country
/// level, the subdivision's own being added when Geo has it, so a missing one answers "not covered".
/// </summary>
internal sealed class BusinessCalendars(ICalendarRulesCatalog rules, IEffectiveCalendars calendars, GeoMetrics metrics)
{
    /// <summary>The policy's rule in the jurisdiction's country, or "not covered".</summary>
    public Result<(CalendarRules Rules, PolicyRule Rule)> RuleOf(Jurisdiction place, BusinessDayPolicy policy)
    {
        var country = CountryCode.Create(place.CountryCode).Value;
        return rules.Find(country) is { } declared && declared.RuleFor(policy) is { } rule
            ? (declared, rule)
            : GeoErrors.BusinessDaysNotCovered(place.Value, BusinessDayPolicies.NameOf(policy));
    }

    /// <summary>The calendar, or "not covered" (counted by country) or another failure.</summary>
    public async Task<Result<LoadedBusinessCalendar>> LoadAsync(
        Jurisdiction place, int year, BusinessDayPolicy policy, CancellationToken cancellationToken)
    {
        var loaded = await LoadCalendarAsync(place, year, policy, cancellationToken);
        if (loaded.Errors.FirstOrDefault(e => e.Code is GeoErrorCodes.HolidaysNotCovered or GeoErrorCodes.BusinessDaysNotCovered) is { } notCovered)
        {
            metrics.NotCovered(place.CountryCode, notCovered.Code);
        }

        return loaded;
    }

    private async Task<Result<LoadedBusinessCalendar>> LoadCalendarAsync(
        Jurisdiction place, int year, BusinessDayPolicy policy, CancellationToken cancellationToken)
    {
        var rule = RuleOf(place, policy);
        if (!rule.IsSuccess)
        {
            return Result.Failure<LoadedBusinessCalendar>(rule.Errors);
        }

        var holidayYear = HolidayYear.Create(year);
        if (!holidayYear.IsSuccess)
        {
            return GeoErrors.HolidaysNotCovered(place.Country.Value, year, CalendarScope.General.Value);
        }

        // Only the country's calendar is required (a subdivision's own is added when Geo has it), so it is the one missing.
        var general = await CoveredCalendar.LoadAsync(calendars, place, holidayYear.Value, CalendarScope.General, cancellationToken);
        if (general is null)
        {
            return GeoErrors.HolidaysNotCovered(place.Country.Value, year, CalendarScope.General.Value);
        }

        List<Observance> sector = [];
        List<Attribution> attributions = [.. general.Attributions];
        foreach (var scope in rule.Value.Rule.SectorScopes)
        {
            if (await CoveredCalendar.LoadAsync(calendars, place, holidayYear.Value, scope, cancellationToken) is not { } covered)
            {
                return GeoErrors.HolidaysNotCovered(place.Country.Value, year, scope.Value);
            }

            sector.AddRange(covered.Observances);
            attributions.AddRange(covered.Attributions);
        }

        var calendar = BusinessCalendar.Of(holidayYear.Value, rule.Value.Rules.Weekend, rule.Value.Rule, general.Observances, sector);
        return new LoadedBusinessCalendar(calendar, general.Covers, [.. attributions.Distinct()]);
    }
}
