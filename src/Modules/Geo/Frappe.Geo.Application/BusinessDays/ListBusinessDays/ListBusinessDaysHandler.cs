using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Geo.Application.BusinessDays.ListBusinessDays;

/// <summary>
/// Walks year by year from the first date; a year Geo has no calendar of answers "not covered", never fewer days. The
/// answer covers and credits every calendar of every year walked.
/// </summary>
internal sealed class ListBusinessDaysHandler(ICalendarRulesCatalog rules, IEffectiveCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, GeoMetrics metrics)
    : IQueryHandler<ListBusinessDays, BusinessDaysView>
{
    /// <summary>About three months of business days: enough for any deadline, and at most two calendar years read.</summary>
    public const int MaxCount = 60;

    public async Task<Result<BusinessDaysView>> HandleAsync(ListBusinessDays query, CancellationToken cancellationToken)
    {
        var from = BusinessDayInput.Date(query.From, "from");
        var policy = BusinessDayInput.Policy(query.Policy);
        var count = BusinessDayInput.Count(query.Count, MaxCount);
        if (!from.IsSuccess || !policy.IsSuccess || !count.IsSuccess)
        {
            return Result.Failure<BusinessDaysView>([.. from.Errors, .. policy.Errors, .. count.Errors]);
        }

        var place = await Jurisdictions.ResolveAsync(query.Jurisdiction, countries, territory, cancellationToken);
        if (!place.IsSuccess)
        {
            return Result.Failure<BusinessDaysView>(place.Errors);
        }

        var business = new BusinessCalendars(rules, calendars, metrics);
        List<LocalDate> dates = [];
        List<string> covers = [];
        List<Attribution> attributions = [];
        var year = from.Value.Year;
        while (dates.Count < count.Value)
        {
            var calendar = await business.LoadAsync(place.Value, year, policy.Value, cancellationToken);
            if (!calendar.IsSuccess)
            {
                return Result.Failure<BusinessDaysView>(calendar.Errors);
            }

            dates.AddRange(calendar.Value.Calendar.BusinessDaysFrom(from.Value).Take(count.Value - dates.Count));
            covers.AddRange(calendar.Value.Covers);
            attributions.AddRange(calendar.Value.Attributions);
            year++;
        }

        return new BusinessDaysView(place.Value.Value, policy.Value, from.Value, dates, [.. covers.Distinct()], [.. attributions.Distinct()]);
    }
}
