namespace Frappe.Access.Contracts;

/// <summary>
/// <see cref="Is.Holder"/>: the caller is the principal the resource's book names as its holder (<c>HeldBy</c>). Failing it hides the
/// resource (<see cref="DenialReason.NotHolder"/>), whichever rule it sits in (S9). A system caller is no principal and never holds anything.
/// </summary>
internal sealed class HolderRule : AnyResourceRule
{
    public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var holds = !context.Caller.IsSystem
            && context.Caller.PrincipalId is { } principal
            && context.HolderId is { } holder
            && principal == holder;
        return ValueTask.FromResult(holds ? Verdict.Allowed : Verdict.Deny(DenialReason.NotHolder));
    }

    public override string Describe() => "Is.Holder";
}
