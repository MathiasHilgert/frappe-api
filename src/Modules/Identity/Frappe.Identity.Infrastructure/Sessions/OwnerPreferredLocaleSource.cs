using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Platform.Localization;
using Frappe.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>
/// Feeds the signed-in owner's stored locale to response negotiation. Reads one column, and only when the request's
/// <c>Accept-Language</c> could not decide (the middleware asks last).
/// </summary>
internal sealed class OwnerPreferredLocaleSource(ICurrentPrincipal current, IdentityDbContext db) : IPreferredLocaleSource
{
    public async ValueTask<Locale?> GetPreferredLocaleAsync(CancellationToken cancellationToken)
    {
        if (current.Session is not { } session)
            return null;

        return await db.Principals.AsNoTracking()
            .Where(p => p.Id == session.PrincipalId)
            .Select(p => p.Owner.Locale)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
