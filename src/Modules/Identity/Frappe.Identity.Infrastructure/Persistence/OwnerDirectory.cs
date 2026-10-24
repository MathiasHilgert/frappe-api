using Frappe.Identity.Contracts.Queries;
using Frappe.Modularity;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>Identity's answer to other modules' contact lookups: one no-tracking projection from its own schema.</summary>
// Public so Wolverine builds it inline in the notification handler.
[InlinedByWolverine]
public sealed class OwnerDirectory(IdentityDbContext context) : IOwnerDirectory
{
    public async Task<OwnerContact?> FindContactAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var row = await context.Principals.AsNoTracking()
            .Where(p => p.Id == principalId)
            .Select(p => new { p.Owner.Name, p.Owner.Email, p.Owner.Locale })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new OwnerContact(row.Name.Value, row.Email.Value, row.Locale.Value);
    }
}
