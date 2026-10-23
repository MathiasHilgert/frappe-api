namespace Frappe.Identity.Infrastructure.Sessions;

public static class IdentityPolicies
{
    /// <summary>An owner acting on their own account through one of their sessions.</summary>
    public const string OwnerSelf = "owner-self";
}
