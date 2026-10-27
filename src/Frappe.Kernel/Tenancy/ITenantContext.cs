namespace Frappe.Kernel.Tenancy;

/// <summary>
/// The tenant and principal the current message runs for.
/// </summary>
public interface ITenantContext
{
    /// <summary>Gets the business (tenant) ID.</summary>
    public Guid BusinessId { get; }

    /// <summary>Gets the branch ID, when the message is scoped to one branch.</summary>
    public Guid? BranchId { get; }

    /// <summary>Gets the ID of the principal acting.</summary>
    public Guid PrincipalId { get; }
}
