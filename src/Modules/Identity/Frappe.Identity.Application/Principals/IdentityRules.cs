using Frappe.Access.Contracts;
using Frappe.Identity.Application.Sessions.AuthenticateSession;

namespace Frappe.Identity.Application.Principals;

/// <summary>The rules Identity's own requests declare, written once so a use case names what it needs instead of how it is checked.</summary>
public static class IdentityRules
{
    /// <summary>The caller is a signed-in owner: the principal kind the owner's own account requests (the profile, the address) are for.</summary>
    public static CallerRule OwnerAccount { get; } = Is.Kind(SessionPrincipal.OwnerKind);
}
