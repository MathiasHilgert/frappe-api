namespace Frappe.Access.Contracts;

/// <summary>What a module declared for one resource type: its holder, its visibility rule and the rule of each action.</summary>
public sealed class ResourceRuleSet
{
    private readonly Dictionary<string, RuleEvaluation> _actions = [with(StringComparer.Ordinal)];

    internal ResourceRuleSet(string notFoundCode) => NotFoundCode = notFoundCode;

    /// <summary>The code a hidden answer carries: the resource's own not-found.</summary>
    public string NotFoundCode { get; }

    /// <summary>The rule that says who may see the resource; nothing until the book declares it (then it is hidden from everyone).</summary>
    public RuleEvaluation? Visible { get; internal set; }

    internal Func<object, Guid>? Holder { get; set; }

    /// <summary>The principal holding <paramref name="resource"/>.</summary>
    public Guid HolderOf(object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return (Holder ?? throw new InvalidOperationException("The resource's holder was never declared."))(resource);
    }

    /// <summary>The rule of the action called <paramref name="name"/>; nothing when the book allows none.</summary>
    public RuleEvaluation? Allowed(string name) => _actions.GetValueOrDefault(name);

    internal void Allow(string name, RuleEvaluation rule)
    {
        if (!_actions.TryAdd(name, rule))
        {
            throw new InvalidOperationException($"The action '{name}' is allowed twice.");
        }
    }
}
