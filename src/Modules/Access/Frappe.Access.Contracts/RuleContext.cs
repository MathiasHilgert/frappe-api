namespace Frappe.Access.Contracts;

/// <summary>
/// What a rule reads to decide: the caller, the clock, the container for facts a module's own pieces ask live (never cached, SEC-17) and, for
/// a resource check, the resource and the principal that holds it.
/// </summary>
public sealed record RuleContext(
    ICaller Caller,
    DateTimeOffset Now,
    IServiceProvider Services,
    object? Resource = null,
    Guid? HolderId = null);
