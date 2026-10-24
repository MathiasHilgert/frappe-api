namespace Frappe.Identity.Infrastructure.Sessions;

internal static class IdentityPolicies
{
    /// <summary>An owner acting on their own account through one of their sessions.</summary>
    public const string OwnerSelf = "owner-self";
}
