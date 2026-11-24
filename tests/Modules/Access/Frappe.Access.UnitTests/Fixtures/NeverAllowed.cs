using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A piece a module adds without touching Access: it denies for its own reason.</summary>
public sealed class NeverAllowed : CallerRule
{
    public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Verdict.Deny(DenialReason.NotAllowed));

    public override string Describe() => "NeverAllowed";
}
