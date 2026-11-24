namespace Frappe.Access.Contracts;

/// <summary>The rules about who the caller is. Anonymous and system callers never pass any of them implicitly.</summary>
public static class Is
{
    /// <summary>The caller proved itself as a principal. An anonymous or system caller fails it (a denial, never a 401).</summary>
    public static CallerRule Authenticated { get; } = new SimpleRule(
        "Is.Authenticated",
        context => context.Caller is { IsSystem: false, PrincipalId: not null } ? Verdict.Allowed : Verdict.Deny(DenialReason.NotAuthenticated));

    /// <summary>The caller is the principal that holds the resource (a resource rule: see <see cref="RuleBook"/>).</summary>
    public static AnyResourceRule Holder { get; } = new HolderRule();

    /// <summary>The caller is an authenticated principal of the given kind, such as <c>owner</c>.</summary>
    public static CallerRule Kind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return new SimpleRule(
            $"Is.Kind({kind})",
            context => context.Caller switch
            {
                { IsSystem: true } or { PrincipalId: null } => Verdict.Deny(DenialReason.NotAuthenticated),
                var caller => string.Equals(caller.Kind, kind, StringComparison.Ordinal) ? Verdict.Allowed : Verdict.Deny(DenialReason.WrongKind),
            });
    }
}
