using Frappe.Identity.Application.Owners;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>Stages owners on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfOwners(IdentityDbContext db) : IOwners
{
    public Task<Owner?> FindAsync(Guid principalId, CancellationToken cancellationToken) =>
        db.Owners.SingleOrDefaultAsync(o => o.PrincipalId == principalId, cancellationToken);

    public void Add(Owner owner) => db.Owners.Add(owner);
}
