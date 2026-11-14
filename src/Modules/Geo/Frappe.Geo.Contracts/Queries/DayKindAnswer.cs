namespace Frappe.Geo.Contracts.Queries;

/// <summary>A date's kind for payroll; <see cref="Kind"/> is <see langword="null"/> for an ordinary day, and nothing holds unless <see cref="Status"/> is covered.</summary>
public sealed record DayKindAnswer(CalendarStatus Status, DayKind? Kind, bool IsWeekend);
