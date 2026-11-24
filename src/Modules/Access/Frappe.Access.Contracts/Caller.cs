namespace Frappe.Access.Contracts;

/// <summary>An immutable snapshot of who is calling: what a host builds from its own proof, and the two callers that have none.</summary>
public sealed record Caller(
    Guid? PrincipalId,
    string? Kind,
    Guid? SessionId,
    DateTimeOffset? StepUpValidUntil,
    bool IsSystem = false) : ICaller
{
    /// <summary>Nobody proved anything: never passes a guarded rule.</summary>
    public static Caller Anonymous { get; } = new(null, null, null, null);

    /// <summary>The platform itself, in the Worker: never passes a guarded rule implicitly.</summary>
    public static Caller System { get; } = new(null, null, null, null, IsSystem: true);
}
