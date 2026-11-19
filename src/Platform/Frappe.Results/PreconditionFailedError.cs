namespace Frappe.Results;

/// <summary>
/// The caller made the operation conditional on the state it last saw (an HTTP <c>If-Match</c>), and the state has
/// changed since: nothing was done. Reading the current state again and retrying may succeed.
/// </summary>
public sealed record PreconditionFailedError : Error
{
    public PreconditionFailedError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
