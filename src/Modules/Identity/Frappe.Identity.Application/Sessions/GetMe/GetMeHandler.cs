using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.SharedKernel.Validation;
using Wolverine.Attributes;

namespace Frappe.Identity.Application.Sessions;

[NonTransactional]
public static class GetMeHandler
{
    public static async Task<Result<OwnerProfile>> Handle(GetMe query, IOwnerReads owners, CancellationToken cancellationToken) =>
        await owners.GetProfileAsync(query.PrincipalId, cancellationToken) is { } profile
            ? profile
            : IdentityErrors.PrincipalGone;
}
