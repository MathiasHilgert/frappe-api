using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A piece a module adds without touching Access: it allows, asynchronously.</summary>
public sealed class AlwaysAllowed : CallerRule
{
    public override async ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return Verdict.Allowed;
    }

    public override string Describe() => "AlwaysAllowed";
}
