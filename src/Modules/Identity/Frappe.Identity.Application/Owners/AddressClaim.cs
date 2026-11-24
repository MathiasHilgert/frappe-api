using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Identifiers;

namespace Frappe.Identity.Application.Owners;

/// <summary>
/// Decides whether an owner may take an address that an email change or an undo puts on them: nobody holds it, or a pending owner does and is
/// deleted with everything of theirs (it never proved the address). A verified holder, or the owner themself, keeps it and the claim is refused.
/// </summary>
public sealed class AddressClaim(IPrincipals principals, IOwners owners, IEmailIdentifiers identifiers)
{
    /// <summary>Whether <paramref name="principalId"/> may take <paramref name="email"/>; a pending holder is staged for deletion.</summary>
    public async Task<bool> TryAsync(Guid principalId, Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var holder = await identifiers.FindByAddressAsync(email.NormalizedAddress, cancellationToken);
        if (holder is null)
        {
            return true;
        }

        var squatter = holder.PrincipalId == principalId ? null : await owners.FindAsync(holder.PrincipalId, cancellationToken);
        if (squatter is not { VerifiedAt: null })
        {
            return false;
        }

        principals.Remove((await principals.FindAsync(holder.PrincipalId, cancellationToken))!);
        return true;
    }
}
