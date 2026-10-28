using Frappe.Kernel.Tenancy;

namespace Frappe.Platform.Messaging;

// Reads the tenant of the current async flow, so one stateless instance serves every scope; asking outside
// an open TenantScope is a bug, so it throws.
internal sealed class AmbientTenantContext : ITenantContext
{
    public Guid BusinessId => Required.BusinessId;

    public Guid? BranchId => Required.BranchId;

    public Guid PrincipalId => Required.PrincipalId;

    private static TenantScope.Tenant Required =>
        TenantScope.Current
        ?? throw new InvalidOperationException("No tenant scope is open for the current message.");
}
