namespace Frappe.Geo.Domain.BusinessDays;

/// <summary>
/// What a business day is for: <see cref="Banking"/> (settlements and payments), <see cref="Labor"/> (payroll: the
/// day's kind matters more than a yes or no) or <see cref="Commercial"/> (suppliers and deadlines). Which observances
/// each one treats as non-working is a country's rule (<see cref="CalendarRules"/>), not the core's.
/// </summary>
public enum BusinessDayPolicy
{
    Banking = 0,
    Labor = 1,
    Commercial = 2,
}
