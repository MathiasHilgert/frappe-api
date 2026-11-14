using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Infrastructure.Holidays;

/// <summary>
/// Where every holiday source's calendars land: each one's imported layer is replaced through the aggregate, which keeps
/// the overrides and announces a change of the effective calendar. A calendar is never deleted: a jurisdiction and year
/// a source stops publishing keeps its last import.
/// </summary>
internal sealed class HolidayTarget(IHolidayCalendars calendars) : ISnapshotTarget<HolidaySnapshot>
{
    public string Kind => SyncKinds.Holidays;

    public async Task<SnapshotChanges> ApplyAsync(HolidaySnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (opened, updated) = (0, 0);
        var conflicts = new List<OverrideConflict>();
        foreach (var import in snapshot.Calendars)
        {
            var calendar = await calendars.FindAsync(import.Jurisdiction, import.Year, import.Scope, cancellationToken);
            if (calendar is null)
            {
                calendar = HolidayCalendar.Open(import.Jurisdiction, import.Year, import.Scope);
                calendars.Add(calendar);
                opened++;
            }
            else if (!calendar.Imported.ToHashSet().SetEquals(import.Observances))
            {
                updated++;
            }

            // CalendarImport.Create already ran the calendar's rules on these observances: this cannot fail.
            calendar.Import(import.Observances, snapshot.Attribution);

            // Read, not dequeued: the save still hands every event over (the outbox takes the changes).
            conflicts.AddRange(calendar.DomainEvents.OfType<ObservanceOverrideConflicted>()
                .Select(c => new OverrideConflict(c.Jurisdiction.Value, c.Year.Value, c.Date, c.Name)));
        }

        return new SnapshotChanges(opened, updated, Retired: 0, Revived: 0, RetiresTooMuch: false) { Conflicts = conflicts.Distinct().ToArray() };
    }
}
