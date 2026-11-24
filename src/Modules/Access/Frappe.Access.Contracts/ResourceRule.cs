using System.Diagnostics.CodeAnalysis;

namespace Frappe.Access.Contracts;

/// <summary>
/// A rule about the caller and one resource of type <typeparamref name="T"/>: what a resource's book declares (<see cref="RuleBook"/>). A
/// <see cref="CallerRule"/> and an <see cref="AnyResourceRule"/> convert to it, so <c>Is.Holder &amp; Has.RecentStepUp</c> and
/// <c>CallerRule &amp; ResourceRule&lt;T&gt;</c> are resource rules; the reverse is not true, so a resource rule cannot be declared on a use
/// case. <c>&amp;</c> binds tighter than <c>|</c>. A module writes its own piece by deriving from it and reading <see cref="ResourceOf"/>.
/// </summary>
[SuppressMessage("Major Code Smell", "S2326", Justification = "CRAFT-10: the type parameter binds the rule to its resource at compile time.")]
public abstract class ResourceRule<T>
    where T : class
{
    /// <summary>Decides for the caller and the resource of <paramref name="context"/>.</summary>
    public abstract ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken);

    /// <summary>The rule as text: what a snapshot test pins.</summary>
    public abstract string Describe();

    /// <summary>True for a <c>|</c> combination, which needs parentheses inside an <c>&amp;</c>.</summary>
    internal virtual bool IsAlternative => false;

    /// <summary>The resource being checked, typed. Throws when the context carries none or another type: that is a programming error.</summary>
    protected static T ResourceOf(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Resource as T ?? throw new InvalidOperationException($"The rule needs a {typeof(T).Name} and the context has none.");
    }

    /// <summary>A rule about the caller alone checks the same way in a resource's book.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the conversion is what lets a caller rule be written in a book.")]
    public static implicit operator ResourceRule<T>(CallerRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new Combined(rule.EvaluateAsync, rule.Describe(), rule.IsAlternative);
    }

    /// <summary>A rule that fits any resource checks the same way in this resource's book.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the conversion is what lets Is.Holder be written in a book.")]
    public static implicit operator ResourceRule<T>(AnyResourceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new Combined(rule.EvaluateAsync, rule.Describe(), rule.IsAlternative);
    }

    /// <summary>Both rules must allow.</summary>
    [SuppressMessage("Usage", "CA2225", Justification = "CRAFT-10: the operators are the rule language.")]
    public static ResourceRule<T> operator &(ResourceRule<T> left, ResourceRule<T> right)
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
    public static ResourceRule<T> operator |(ResourceRule<T> left, ResourceRule<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new Combined(
            RuleCombination.Any(left.EvaluateAsync, right.EvaluateAsync),
            RuleCombination.DescribeAny(left.Describe(), right.Describe()),
            alternative: true);
    }

    /// <summary>Two rules joined by <c>&amp;</c> or <c>|</c>, or a caller rule seen as a resource rule.</summary>
    private sealed class Combined(RuleEvaluation evaluation, string description, bool alternative) : ResourceRule<T>
    {
        internal override bool IsAlternative => alternative;

        public override ValueTask<Verdict> EvaluateAsync(RuleContext context, CancellationToken cancellationToken) =>
            evaluation(context, cancellationToken);

        public override string Describe() => description;
    }
}
