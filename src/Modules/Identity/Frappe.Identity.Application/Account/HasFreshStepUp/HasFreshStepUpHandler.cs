using Frappe.Identity.Application.Principals;
using Wolverine.Attributes;

namespace Frappe.Identity.Application.Account;

[NonTransactional]
public static class HasFreshStepUpHandler
{
    public static Task<bool> Handle(
        HasFreshStepUp query, IOwnerReads owners, TimeProvider clock, CancellationToken cancellationToken) =>
        owners.HasFreshStepUpAsync(query.PrincipalId, query.SessionId, clock.GetUtcNow(), cancellationToken);
}
