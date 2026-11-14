namespace Frappe.Geo.Contracts.Queries;

/// <summary>What a business day is for: settlements and payments, payroll, or suppliers and deadlines.</summary>
public enum BusinessDayPolicy
{
    Banking = 0,
    Labor = 1,
    Commercial = 2,
}
