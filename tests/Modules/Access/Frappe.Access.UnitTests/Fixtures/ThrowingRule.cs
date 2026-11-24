using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A piece of a module that cannot decide: it throws, as a fact store that is down would.</summary>
public sealed class ThrowingRule : CallerRule
{
    public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("secret detail: user@example.com");

    public override string Describe() => "Throws";
}
