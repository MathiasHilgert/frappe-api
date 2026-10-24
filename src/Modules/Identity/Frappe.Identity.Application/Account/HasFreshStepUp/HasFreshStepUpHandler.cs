using Frappe.Identity.Application.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine.Attributes;

namespace Frappe.Identity.Application.Account;

[NonTransactional]
public static class HasFreshStepUpHandler
{
    public static async Task<Result<bool>> Handle(
        HasFreshStepUp query, IOwnerReads owners, TimeProvider clock, CancellationToken cancellationToken) =>
        await owners.HasFreshStepUpAsync(query.PrincipalId, query.SessionId, clock.GetUtcNow(), cancellationToken);
}
