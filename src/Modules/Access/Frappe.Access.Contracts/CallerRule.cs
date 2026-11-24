using System.Diagnostics.CodeAnalysis;

namespace Frappe.Access.Contracts;

/// <summary>
/// A rule about the caller alone: what a use case declares in <see cref="IGuarded.Allowed"/>. A module writes its own pieces by deriving from
/// it, and combines pieces with <c>&amp;</c> and <c>|</c>; <c>&amp;</c> binds tighter than <c>|</c> (as in C#), so
/// <c>A | B &amp; C</c> is <c>A | (B &amp; C)</c>, and <see cref="Describe"/> shows the combination with its parentheses. Each piece evaluates
/// itself, asynchronously, reading its facts live. A resource rule (<see cref="ResourceRule{T}"/>) is not a caller rule, so the compiler refuses
/// one on a use case.
/// </summary>
public abstract class CallerRule
{
    /// <summary>Decides for the caller of <paramref name="context"/>.</summary>
    public abstract ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken);

    /// <summary>The rule as text, such as <c>Is.Authenticated &amp; Has.RecentStepUp</c>: what a snapshot test pins.</summary>
    public abstract string Describe();

    /// <summary>True for a <c>|</c> combination, which needs parentheses inside an <c>&amp;</c>.</summary>
    internal virtual bool IsAlternative => false;

    /// <summary>Both rules must allow.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static CallerRule operator &(CallerRule left, CallerRule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new Combined(
            RuleCombination.All(left.EvaluateAsync, right.EvaluateAsync),
            RuleCombination.DescribeAll(left.Describe(), left.IsAlternative, right.Describe(), right.IsAlternative),
            alternative: false);
    }

    /// <summary>One of the rules must allow.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static CallerRule operator |(CallerRule left, CallerRule right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new Combined(
            RuleCombination.Any(left.EvaluateAsync, right.EvaluateAsync),
            RuleCombination.DescribeAny(left.Describe(), right.Describe()),
            alternative: true);
    }

    /// <summary>Two rules joined by <c>&amp;</c> or <c>|</c>.</summary>
    private sealed class Combined(RuleEvaluation evaluation, string description, bool alternative) : CallerRule
    {
        internal override bool IsAlternative => alternative;

        public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken) =>
            evaluation(context, cancellationToken);

        public override string Describe() => description;
    }
}
