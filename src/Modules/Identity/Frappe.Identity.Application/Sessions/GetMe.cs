using Frappe.Identity.Application.Principals;

namespace Frappe.Identity.Application.Sessions;

/// <summary>The signed-in owner's own profile.</summary>
public sealed class GetMe(IPrincipalRepository principals)
{
    public async Task<OwnerProfile?> ExecuteAsync(AuthenticatedSession current, CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(current.PrincipalId, cancellationToken);
        return principal is null
            ? null
            : new OwnerProfile(
                principal.Id,
                principal.Owner.Name.Value,
                principal.Owner.Email.Value,
                principal.Owner.Status.ToString().ToLowerInvariant(),
                principal.Owner.Locale.Value);
    }
}
