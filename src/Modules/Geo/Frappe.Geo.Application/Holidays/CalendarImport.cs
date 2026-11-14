using Frappe.Geo.Domain.Holidays;
using Frappe.Results;

namespace Frappe.Geo.Application.Holidays;

/// <summary>What a source publishes for one jurisdiction and year: the whole imported layer of its calendar.</summary>
public sealed record CalendarImport
{
    private CalendarImport(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, IReadOnlyList<Observance> observances)
    {
        Jurisdiction = jurisdiction;
        Year = year;
        Scope = scope;
        Observances = observances;
    }

    public Jurisdiction Jurisdiction { get; }

    public HolidayYear Year { get; }

    /// <summary>Which of the jurisdiction's calendars of the year the source publishes: the general one unless it says otherwise.</summary>
    public CalendarScope Scope { get; }

    public IReadOnlyList<Observance> Observances { get; }

    /// <summary>
    /// An import the calendar will accept: checked by the calendar's own rules (every observance in the year, no
    /// duplicate), so a source learns of a bad download while reading it, never while it is being applied.
    /// </summary>
    public static Result<CalendarImport> Create(Jurisdiction jurisdiction, HolidayYear year, IReadOnlyList<Observance> observances) =>
        Create(jurisdiction, year, CalendarScope.General, observances);

    /// <summary>An import of a sector's calendar (<paramref name="scope"/>), checked by the same rules.</summary>
    public static Result<CalendarImport> Create(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, IReadOnlyList<Observance> observances)
    {
        var accepted = HolidayCalendar.Open(jurisdiction, year, scope).Import(observances);
        return accepted.IsSuccess ? new CalendarImport(jurisdiction, year, scope, observances) : Result.Failure<CalendarImport>(accepted.Errors);
    }
}
