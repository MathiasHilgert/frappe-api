using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Territory;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime.Text;

namespace Frappe.Geo.Application.Holidays.RemoveObservanceOverride;

internal sealed class RemoveObservanceOverrideHandler(IHolidayCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, ICalendarRulesCatalog rules)
    : ICommandHandler<RemoveObservanceOverride, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveObservanceOverride command, CancellationToken cancellationToken)
    {
        var date = ObservanceInput.Date(command.Date);
        var name = ObservanceInput.Name(command.Name);
        if (!date.IsSuccess || !name.IsSuccess)
        {
            return Result.Failure<Unit>([.. date.Errors, .. name.Errors]);
        }

        var target = await new CalendarToEdit(calendars, countries, territory, rules).FindAsync(command.Jurisdiction, date.Value, command.Scope, cancellationToken);
        if (!target.IsSuccess)
        {
            return Result.Failure<Unit>(target.Errors);
        }

        var calendar = target.Value.Calendar;
        return calendar.RemoveOverride(date.Value, name.Value)
            ? Unit.Value
            : GeoErrors.HolidayOverrideNotFound(calendar.Jurisdiction.Value, LocalDatePattern.Iso.Format(date.Value), name.Value);
    }
}
