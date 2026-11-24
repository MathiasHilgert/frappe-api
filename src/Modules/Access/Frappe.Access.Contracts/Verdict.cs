namespace Frappe.Access.Contracts;

/// <summary>What a rule answers: allowed, or denied with one <see cref="DenialReason"/>.</summary>
public readonly record struct Verdict
{
    private Verdict(DenialReason? reason) => Reason = reason;

    /// <summary>The rule passes.</summary>
    public static Verdict Allowed { get; } = new(null);

    /// <summary>The reason of a denial; nothing when the rule passes.</summary>
    public DenialReason? Reason { get; }

    /// <summary>True when the rule passes.</summary>
    public bool IsAllowed => Reason is null;

    /// <summary>The rule denies for <paramref name="reason"/>.</summary>
    public static Verdict Deny(DenialReason reason) => new(reason);

    /// <summary>
    /// How much a denial weighs when rules combine: hidden (<see cref="DenialReason.NotHolder"/>) over forbidden over step-up, so the answer
    /// never depends on the order the pieces are written in (S9).
    /// </summary>
    internal int Severity => Reason switch
    {
        null => 0,
        DenialReason.StepUpRequired => 1,
        DenialReason.NotAuthenticated or DenialReason.WrongKind or DenialReason.NotAllowed => 2,
        DenialReason.NotHolder => 3,
        _ => throw new System.Diagnostics.UnreachableException($"Unknown reason {Reason}."),
    };
}
