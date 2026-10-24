using Frappe.Identity.Application.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine.Attributes;

namespace Frappe.Identity.Application.Sessions;

[NonTransactional]
public static class ListOwnerSessionsHandler
{
    public static async Task<Result<IReadOnlyList<SessionSummary>>> Handle(
        ListOwnerSessions query, IOwnerReads owners, TimeProvider clock, CancellationToken cancellationToken) =>
        Result.Success(await owners.ListLiveSessionsAsync(query.PrincipalId, query.CurrentSessionId, clock.GetUtcNow(), cancellationToken));
}
