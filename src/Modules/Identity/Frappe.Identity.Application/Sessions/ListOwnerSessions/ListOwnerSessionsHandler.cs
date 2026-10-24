using Frappe.Identity.Application.Principals;
using Wolverine.Attributes;

namespace Frappe.Identity.Application.Sessions;

[NonTransactional]
public static class ListOwnerSessionsHandler
{
    public static Task<IReadOnlyList<SessionSummary>> Handle(
        ListOwnerSessions query, IOwnerReads owners, TimeProvider clock, CancellationToken cancellationToken) =>
        owners.ListLiveSessionsAsync(query.PrincipalId, query.CurrentSessionId, clock.GetUtcNow(), cancellationToken);
}
