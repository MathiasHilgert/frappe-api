namespace Frappe.Geo.Contracts.Queries;

/// <summary>Whether a calendar answer holds: Geo covers it, does not hold the calendars it needs, or the jurisdiction does not exist.</summary>
public enum CalendarStatus
{
    Covered = 0,
    NotCovered = 1,
    UnknownJurisdiction = 2,
}
