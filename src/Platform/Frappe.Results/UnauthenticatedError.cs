namespace Frappe.Results;

/// <summary>
/// The caller did not prove who they are: no credential, or one that is wrong, expired or revoked. Proving it may
/// succeed where this failed.
/// </summary>
public sealed record UnauthenticatedError : Error
{
    public UnauthenticatedError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
