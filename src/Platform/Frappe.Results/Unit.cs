namespace Frappe.Results;

/// <summary>The result type of operations that succeed without producing a value.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
