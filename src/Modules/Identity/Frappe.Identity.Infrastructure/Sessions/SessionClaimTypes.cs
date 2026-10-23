namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>The claims an authenticated owner session carries, besides the principal id as the name identifier.</summary>
public static class SessionClaimTypes
{
    public const string SessionId = "sid";
    public const string PrincipalType = "principal_type";
}
