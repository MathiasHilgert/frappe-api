namespace Frappe.Results;

/// <summary>The use case cannot proceed because of the current state (a duplicate, a concurrent change).</summary>
public sealed record ConflictError : Error
{
    public ConflictError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
