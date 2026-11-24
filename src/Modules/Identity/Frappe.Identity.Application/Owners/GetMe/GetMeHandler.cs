using Frappe.Identity.Domain;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.GetMe;

/// <summary>Handles <see cref="GetMe"/>; an owner that vanished since the request authenticated is answered like any invalid session.</summary>
internal sealed class GetMeHandler(IOwnerReads reads) : IQueryHandler<GetMe, OwnerProfile>
{
    public async Task<Result<OwnerProfile>> HandleAsync(GetMe query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var profile = await reads.FindProfileAsync(query.PrincipalId, cancellationToken);
        return profile is null ? new UnauthenticatedError(IdentityErrorCodes.SessionInvalid) : profile;
    }
}
