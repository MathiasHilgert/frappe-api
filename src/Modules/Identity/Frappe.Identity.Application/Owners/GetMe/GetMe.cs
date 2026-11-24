using Frappe.Access.Contracts;
using Frappe.Identity.Application.Principals;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.GetMe;

/// <summary>The caller's own profile (S10).</summary>
public sealed record GetMe : IQuery<OwnerProfile>, IGuarded
{
    public static CallerRule Allowed => IdentityRules.OwnerAccount;
}
