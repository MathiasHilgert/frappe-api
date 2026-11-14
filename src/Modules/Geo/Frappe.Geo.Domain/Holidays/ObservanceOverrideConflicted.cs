using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// The source changed an observance a manual override decides: the override still wins, and someone should look at it.
/// </summary>
public sealed record ObservanceOverrideConflicted(Jurisdiction Jurisdiction, HolidayYear Year, LocalDate Date, string Name) : IDomainEvent;
