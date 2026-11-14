using Frappe.IntegrationEvents;

namespace Frappe.Geo.Contracts.Holidays;

/// <summary>
/// The effective holidays of <see cref="Jurisdiction"/> (<c>AR</c>, or a subdivision such as <c>AR-X</c>) in
/// <see cref="Year"/> changed: a source published a different calendar, someone overrode it, or Geo stored that
/// calendar for the first time (even empty: a year Geo had no data for is now covered). Published only when what counts
/// changed, so a consumer can drop what it derived from that calendar, including a "not covered" answer.
/// </summary>
[IntegrationEvent("frappe.geo.holidays_changed.v1")]
public sealed record HolidaysChanged(string Jurisdiction, int Year) : IIntegrationEvent
{
    /// <summary>
    /// Which of the jurisdiction's calendars of that year changed: <c>general</c>, or a sector's (<c>banking</c>).
    /// Absent in events published before it existed: then any of them may have changed.
    /// </summary>
    public string? Scope { get; init; }
}
