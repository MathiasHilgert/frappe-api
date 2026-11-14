namespace Frappe.Geo.Contracts.Queries;

/// <summary>A date under a policy; <see cref="IsBusinessDay"/>, <see cref="IsWeekend"/> and <see cref="Kind"/> hold only when <see cref="Status"/> is covered.</summary>
public sealed record BusinessDayAnswer(CalendarStatus Status, bool IsBusinessDay, bool IsWeekend, DayKind? Kind);
