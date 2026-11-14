namespace Frappe.Geo.Domain.Holidays;

/// <summary>Where an entry of a calendar comes from: the source, or a manual override that adds or suppresses an observance.</summary>
public enum CalendarLayer
{
    Imported = 0,
    Added = 1,
    Suppressed = 2,
}
