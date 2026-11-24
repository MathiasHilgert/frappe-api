namespace Frappe.Access.Contracts;

/// <summary>The last step of a resource's rules: what each action needs. An action with no rule is hidden. See <see cref="RuleBook"/>.</summary>
public sealed class VisibleResource<T>
    where T : class
{
    private readonly ResourceRuleSet _set;

    internal VisibleResource(ResourceRuleSet set) => _set = set;

    /// <summary>Who may do <paramref name="action"/>. A caller that fails it is answered forbidden, or step-up required.</summary>
    public VisibleResource<T> Allow(ResourceAction<T> action, ResourceRule<T> rule)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(rule);
        _set.Allow(action.Name, rule.EvaluateAsync);
        return this;
    }
}
