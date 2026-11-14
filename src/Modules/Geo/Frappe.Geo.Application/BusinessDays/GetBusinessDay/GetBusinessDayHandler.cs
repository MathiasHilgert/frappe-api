using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Territory;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.BusinessDays.GetBusinessDay;

internal sealed class GetBusinessDayHandler(ICalendarRulesCatalog rules, IEffectiveCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, GeoMetrics metrics)
    : IQueryHandler<GetBusinessDay, BusinessDayView>
{
    public async Task<Result<BusinessDayView>> HandleAsync(GetBusinessDay query, CancellationToken cancellationToken)
    {
        var date = BusinessDayInput.Date(query.Date, "date");
        var policy = BusinessDayInput.Policy(query.Policy);
        if (!date.IsSuccess || !policy.IsSuccess)
        {
            return Result.Failure<BusinessDayView>([.. date.Errors, .. policy.Errors]);
        }

        var place = await Jurisdictions.ResolveAsync(query.Jurisdiction, countries, territory, cancellationToken);
        if (!place.IsSuccess)
        {
            return Result.Failure<BusinessDayView>(place.Errors);
        }

        var calendar = await new BusinessCalendars(rules, calendars, metrics).LoadAsync(place.Value, date.Value.Year, policy.Value, cancellationToken);
        return calendar.IsSuccess
            ? new BusinessDayView(place.Value.Value, policy.Value, calendar.Value.Calendar.DayOf(date.Value), calendar.Value.Covers, calendar.Value.Attributions)
            : Result.Failure<BusinessDayView>(calendar.Errors);
    }
}
