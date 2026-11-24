using System.Linq.Expressions;

namespace Frappe.Access.Contracts;

/// <summary>The first step of a resource's rules: who holds it. See <see cref="RuleBook"/>.</summary>
public sealed class ResourceDeclaration<T>
    where T : class
{
    private readonly ResourceRuleSet _set;

    internal ResourceDeclaration(ResourceRuleSet set) => _set = set;

    /// <summary>
    /// The principal that holds the resource. It is an expression, not a delegate, so the same declaration can later feed a list filter
    /// (S3); today it is compiled once.
    /// </summary>
    public HeldResource<T> HeldBy(Expression<Func<T, Guid>> holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        var compiled = holder.Compile();
        _set.Holder = resource => compiled((T)resource);
        return new HeldResource<T>(_set);
    }
}
