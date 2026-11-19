namespace Frappe.Results;

/// <summary>
/// The caller is known but may not perform the operation. Proving who they are again changes nothing.
/// </summary>
public sealed record ForbiddenError : Error
{
    public ForbiddenError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
