using System.Security.Claims;

namespace Frappe.Application.Abstractions.Realtime;

/// <summary>
/// Whether a signed-in caller belongs to a business, so it may read that business's realtime stream. The platform
/// denies by default: until a business module implements this port (replacing the platform's registration), every
/// stream request answers 403.
/// </summary>
public interface IBusinessMembership
{
    Task<bool> IsMemberAsync(ClaimsPrincipal user, Guid businessId, CancellationToken cancellationToken);
}
