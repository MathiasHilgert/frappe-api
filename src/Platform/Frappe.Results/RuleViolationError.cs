namespace Frappe.Results;

/// <summary>A business rule or an invariant refused the operation, although every value was valid on its own.</summary>
public sealed record RuleViolationError : Error
{
    public RuleViolationError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
