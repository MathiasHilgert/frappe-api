using Frappe.Access.Contracts;
using Frappe.Identity.Application.Principals;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.RequestEmailChange;

/// <summary>
/// Asks to change the caller's address (S15): a code goes to the new address and the old one is told. The answer is the same whether the new
/// address is free or held by someone else (SEC-06): only the code is not sent in the second case. The route asks for a stepped-up session.
/// </summary>
/// <param name="NewEmail">The address the owner wants, as typed.</param>
public sealed record RequestEmailChange(string? NewEmail) : ICommand<EmailChangeRequested>, IGuarded
{
    public static CallerRule Allowed => IdentityRules.OwnerAccount & Has.RecentStepUp;

    public override string ToString() => $"RequestEmailChange {{ the address is hidden }}";
}
