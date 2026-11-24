using System.Globalization;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>Reads owners untracked, straight into the profile a query answers with (CQRS-08), in one statement.</summary>
internal sealed class EfOwnerReads(IdentityDbContext db) : IOwnerReads
{
    public async Task<OwnerProfile?> FindProfileAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var row = await db.Owners.AsNoTracking()
            .Where(o => o.PrincipalId == principalId)
            .Join(db.Principals, o => o.PrincipalId, p => p.Id, (o, p) => new { Owner = o, p.Kind })
            .Join(db.EmailIdentifiers, x => x.Owner.PrincipalId, e => e.PrincipalId, (x, e) => new
            {
                x.Owner.PrincipalId,
                x.Kind,
                x.Owner.GivenName,
                x.Owner.FamilyName,
                e.Address,
                x.Owner.Locale,
                Version = EF.Property<uint>(x.Owner, "xmin"),
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new OwnerProfile(
                row.PrincipalId, StoredEnum<Domain.Principals.PrincipalKind>.ToText(row.Kind), row.GivenName, row.FamilyName, row.Address,
                row.Locale.Value, row.Version.ToString(CultureInfo.InvariantCulture));
    }
}
