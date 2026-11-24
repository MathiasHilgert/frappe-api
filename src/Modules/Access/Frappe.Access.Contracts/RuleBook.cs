namespace Frappe.Access.Contracts;

/// <summary>
/// Every resource's rules, declared by the modules through <see cref="IRules"/>: <c>book.For&lt;Session&gt;(notFound: code).HeldBy(s =&gt;
/// s.PrincipalId).Visible(Is.Holder).Allow(action, rule)</c>. The steps come in that order (the compiler enforces it): who holds it, who may
/// see it, then what each action needs.
/// </summary>
public sealed class RuleBook
{
    private readonly Dictionary<Type, ResourceRuleSet> _sets = [];

    /// <summary>Begins the rules of <typeparamref name="T"/>; <paramref name="notFound"/> is the code its hidden answer carries.</summary>
    public ResourceDeclaration<T> For<T>(string notFound)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notFound);
        var set = new ResourceRuleSet(notFound);
        return _sets.TryAdd(typeof(T), set)
            ? new ResourceDeclaration<T>(set)
            : throw new InvalidOperationException($"The rules of {typeof(T).Name} are declared twice.");
    }

    /// <summary>The rules of <paramref name="resource"/>; false when no module declared any.</summary>
    public bool TryGet(Type resource, out ResourceRuleSet set)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return _sets.TryGetValue(resource, out set!);
    }
}
