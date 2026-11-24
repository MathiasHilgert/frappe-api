using System.Diagnostics.CodeAnalysis;

namespace Frappe.Access.Contracts;

/// <summary>
/// A rule that reads a resource without knowing its type, such as <see cref="Is.Holder"/>: it fits the book of every resource
/// (it converts to any <see cref="ResourceRule{T}"/>) and cannot be declared on a use case. Combine it with caller rules and other resource
/// rules with <c>&amp;</c> and <c>|</c>.
/// </summary>
public abstract class AnyResourceRule
{
    /// <summary>Decides for the caller and the resource of <paramref name="context"/>.</summary>
    public abstract ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken);

    /// <summary>The rule as text: what a snapshot test pins.</summary>
    public abstract string Describe();

    /// <summary>True for a <c>|</c> combination, which needs parentheses inside an <c>&amp;</c>.</summary>
    internal virtual bool IsAlternative => false;

    /// <summary>Both rules must allow.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static AnyResourceRule operator &(AnyResourceRule left, AnyResourceRule right) => All(left, right);

    /// <summary>The same combination with a caller rule on one side.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static AnyResourceRule operator &(AnyResourceRule left, CallerRule right) => All(left, right);

    /// <summary>The same combination with a caller rule on one side.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static AnyResourceRule operator &(CallerRule left, AnyResourceRule right) => All(left, right);

    /// <summary>One of the rules must allow.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static AnyResourceRule operator |(AnyResourceRule left, AnyResourceRule right) => Any(left, right);

    /// <summary>The same combination with a caller rule on one side.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static AnyResourceRule operator |(AnyResourceRule left, CallerRule right) => Any(left, right);

    /// <summary>The same combination with a caller rule on one side.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static AnyResourceRule operator |(CallerRule left, AnyResourceRule right) => Any(left, right);

    private static Combined All(AnyResourceRule left, AnyResourceRule right) => new(
        RuleCombination.All(left.EvaluateAsync, right.EvaluateAsync),
        RuleCombination.DescribeAll(left.Describe(), left.IsAlternative, right.Describe(), right.IsAlternative),
        alternative: false);

    private static Combined All(AnyResourceRule left, CallerRule right) => new(
        RuleCombination.All(left.EvaluateAsync, right.EvaluateAsync),
        RuleCombination.DescribeAll(left.Describe(), left.IsAlternative, right.Describe(), right.IsAlternative),
        alternative: false);

    private static Combined All(CallerRule left, AnyResourceRule right) => new(
        RuleCombination.All(left.EvaluateAsync, right.EvaluateAsync),
        RuleCombination.DescribeAll(left.Describe(), left.IsAlternative, right.Describe(), right.IsAlternative),
        alternative: false);

    private static Combined Any(AnyResourceRule left, AnyResourceRule right) => new(
        RuleCombination.Any(left.EvaluateAsync, right.EvaluateAsync),
        RuleCombination.DescribeAny(left.Describe(), right.Describe()),
        alternative: true);

    private static Combined Any(AnyResourceRule left, CallerRule right) => new(
        RuleCombination.Any(left.EvaluateAsync, right.EvaluateAsync),
        RuleCombination.DescribeAny(left.Describe(), right.Describe()),
        alternative: true);

    private static Combined Any(CallerRule left, AnyResourceRule right) => new(
        RuleCombination.Any(left.EvaluateAsync, right.EvaluateAsync),
        RuleCombination.DescribeAny(left.Describe(), right.Describe()),
        alternative: true);

    /// <summary>Two rules joined by <c>&amp;</c> or <c>|</c>.</summary>
    private sealed class Combined(RuleEvaluation evaluation, string description, bool alternative) : AnyResourceRule
    {
        internal override bool IsAlternative => alternative;

        public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken) =>
            evaluation(context, cancellationToken);

        public override string Describe() => description;
    }
}
