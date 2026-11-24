using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Principals;

/// <summary>Stages principals on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfPrincipals(IdentityDbContext db) : IPrincipals
{
    public Task<Principal?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Principals.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(Principal principal) => db.Principals.Add(principal);

    public void Remove(Principal principal) => db.Principals.Remove(principal);
}
