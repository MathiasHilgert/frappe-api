namespace Frappe.Results;

/// <summary>The use case looked for something that does not exist.</summary>
public sealed record NotFoundError : Error
{
    public NotFoundError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
