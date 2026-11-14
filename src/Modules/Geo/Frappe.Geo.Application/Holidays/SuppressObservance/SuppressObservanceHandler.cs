using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Territory;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.SuppressObservance;

/// <summary>Only an observance the source publishes can be suppressed (422 <c>geo.observance.not_in_source</c> otherwise).</summary>
internal sealed class SuppressObservanceHandler(IHolidayCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, ICalendarRulesCatalog rules)
    : ICommandHandler<SuppressObservance, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SuppressObservance command, CancellationToken cancellationToken)
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

        var changed = target.Value.Calendar.Suppress(date.Value, name.Value);
        return changed.IsSuccess ? Unit.Value : Result.Failure<Unit>(changed.Errors);
    }
}
