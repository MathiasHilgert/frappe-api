namespace Frappe.Platform.Messaging;

/// <summary>
/// The tenant a non-HTTP caller (a consumer, a job) runs for, flowing with the async context until disposed.
/// Only the platform opens one.
/// </summary>
internal sealed class TenantScope : IDisposable
{
    // shortcut: until the security PR adds the Kernel Principal.System type, background work runs as this id.
    public static readonly Guid SystemPrincipalId = Guid.Empty;

    private static readonly AsyncLocal<Tenant?> _current = new();

    private readonly Tenant? _previous;

    private TenantScope(Tenant? tenant)
    {
        _previous = _current.Value;
        _current.Value = tenant;
    }

    /// <summary>Gets the tenant of the current async flow, or <see langword="null"/> outside any scope.</summary>
    public static Tenant? Current => _current.Value;

    public static TenantScope Begin(Guid businessId, Guid? branchId, Guid principalId) =>
        new(new Tenant(businessId, branchId, principalId));

    public void Dispose() => _current.Value = _previous;

    internal sealed record Tenant(Guid BusinessId, Guid? BranchId, Guid PrincipalId);
}
