namespace Frappe.Results;

/// <summary>A value object rejected its input (wrong format, out of range, empty).</summary>
public sealed record InvalidValueError : Error
{
    public InvalidValueError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
