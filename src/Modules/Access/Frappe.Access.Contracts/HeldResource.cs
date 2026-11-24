namespace Frappe.Access.Contracts;

/// <summary>The second step of a resource's rules: who may see it. See <see cref="RuleBook"/>.</summary>
public sealed class HeldResource<T>
    where T : class
{
    private readonly ResourceRuleSet _set;

    internal HeldResource(ResourceRuleSet set) => _set = set;

    /// <summary>
    /// Who may see the resource. A caller that fails it is answered the resource's own not-found, whatever the rule order, so it learns nothing
    /// about whether the resource exists.
    /// </summary>
    public VisibleResource<T> Visible(ResourceRule<T> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _set.Visible = rule.EvaluateAsync;
        return new VisibleResource<T>(_set);
    }
}
