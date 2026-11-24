using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.ModuleName.Application.AreaName.UseCaseName;

/// <summary>
/// FILL IN: say who does what: <paramref name="PrincipalId"/> (the principal of the session) ... and what the answer is.
/// </summary>
//#if (IsQuery)
public sealed record UseCaseName(Guid PrincipalId) : IQuery<Unit>, IGuarded
{
    // FILL IN: who may run it (Is.Authenticated, a module rule & Has.RecentStepUp ...), or replace IGuarded with IPublic and name its proof.
    public static CallerRule Allowed => Is.Authenticated;
}
//#else
public sealed record UseCaseName(Guid PrincipalId) : ICommand<Unit>, IGuarded
{
    // FILL IN: who may run it (Is.Authenticated, a module rule & Has.RecentStepUp ...), or replace IGuarded with IPublic and name its proof.
    public static CallerRule Allowed => Is.Authenticated;
}
//#endif
