namespace Frappe.Access.Contracts;

/// <summary>How rules combine, written once for every kind of rule: <c>&amp;</c> and <c>|</c>, and how a combination describes itself.</summary>
internal static class RuleCombination
{
    /// <summary>
    /// Every piece must allow. Both pieces are read (unless the first already hides the resource) and the heaviest denial wins, so the
    /// answer does not depend on the order the pieces are written in (S9).
    /// </summary>
    public static RuleEvaluation All(RuleEvaluation left, RuleEvaluation right) => async (context, cancellationToken) =>
    {
        var first = await left(context, cancellationToken);
        if (first.Reason == DenialReason.NotHolder)
        {
            return first;
        }

        var second = await right(context, cancellationToken);
        return second.Severity > first.Severity ? second : first;
    };

    /// <summary>
    /// One piece allowing is enough. When none does, the lightest denial is the answer: it is the alternative the caller is closest to
    /// satisfying (a missing step-up over a plain refusal).
    /// </summary>
    public static RuleEvaluation Any(RuleEvaluation left, RuleEvaluation right) => async (context, cancellationToken) =>
    {
        var first = await left(context, cancellationToken);
        if (first.IsAllowed)
        {
            return first;
        }

        var second = await right(context, cancellationToken);
        return second.Severity < first.Severity ? second : first;
    };

    /// <summary>The text of <c>left &amp; right</c>: an alternative inside it keeps its parentheses.</summary>
    public static string DescribeAll(string left, bool leftIsAlternative, string right, bool rightIsAlternative) =>
        $"{(leftIsAlternative ? $"({left})" : left)} & {(rightIsAlternative ? $"({right})" : right)}";

    /// <summary>The text of <c>left | right</c>.</summary>
    public static string DescribeAny(string left, string right) => $"{left} | {right}";
}
