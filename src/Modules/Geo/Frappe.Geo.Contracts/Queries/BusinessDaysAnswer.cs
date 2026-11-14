namespace Frappe.Geo.Contracts.Queries;

/// <summary>The next business days, in order; empty unless <see cref="Status"/> is covered.</summary>
public sealed record BusinessDaysAnswer(CalendarStatus Status, IReadOnlyList<DateOnly> Dates);
