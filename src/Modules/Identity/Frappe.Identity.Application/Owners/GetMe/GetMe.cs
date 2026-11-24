using Frappe.Access.Contracts;
using Frappe.Identity.Application.Principals;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.GetMe;

/// <summary>The caller's own profile (S10).</summary>
/// <param name="PrincipalId">The owner, from the authenticated request, never from the URL (HTTP-08).</param>
public sealed record GetMe(Guid PrincipalId) : IQuery<OwnerProfile>, IGuarded
{
    public static CallerRule Allowed => IdentityRules.OwnerAccount;
}
