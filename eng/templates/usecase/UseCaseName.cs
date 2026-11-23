using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.ModuleName.Application.AreaName.UseCaseName;

/// <summary>
/// FILL IN: say who does what: <paramref name="PrincipalId"/> (the principal of the session) ... and what the answer is.
/// </summary>
//#if (IsQuery)
public sealed record UseCaseName(Guid PrincipalId) : IQuery<Unit>;
//#else
public sealed record UseCaseName(Guid PrincipalId) : ICommand<Unit>;
//#endif
