namespace Frappe.Access.Contracts;

/// <summary>A rule that decides from the caller alone, synchronously: the shape of every piece Access itself ships.</summary>
internal sealed class SimpleRule(string description, Func<RuleContext, Verdict> decide) : CallerRule
{
    public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(decide(context));
    }

    public override string Describe() => description;
}
