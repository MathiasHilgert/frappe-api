namespace Frappe.Kernel.Tenancy;

/// <summary>
/// The tenant and principal the current message runs for.
/// </summary>
public interface ITenantContext
{
    /// <summary>Gets the business (tenant) ID.</summary>
    Guid BusinessId { get; }

    /// <summary>Gets the branch ID, when the message is scoped to one branch.</summary>
    Guid? BranchId { get; }

    /// <summary>Gets the ID of the principal acting.</summary>
    Guid PrincipalId { get; }
}
