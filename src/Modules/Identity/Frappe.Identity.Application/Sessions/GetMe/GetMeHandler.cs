using Frappe.Identity.Application.Principals;
using Wolverine.Attributes;

namespace Frappe.Identity.Application.Sessions;

[NonTransactional]
public static class GetMeHandler
{
    public static Task<OwnerProfile?> Handle(GetMe query, IOwnerReads owners, CancellationToken cancellationToken) =>
        owners.GetProfileAsync(query.PrincipalId, cancellationToken);
}
