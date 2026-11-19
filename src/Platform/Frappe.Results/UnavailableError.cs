namespace Frappe.Results;

/// <summary>
/// A dependency the use case needs is not configured or not answering (an external provider without credentials, a
/// timeout, an outage). The request may succeed later, unchanged.
/// </summary>
public sealed record UnavailableError : Error
{
    public UnavailableError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
