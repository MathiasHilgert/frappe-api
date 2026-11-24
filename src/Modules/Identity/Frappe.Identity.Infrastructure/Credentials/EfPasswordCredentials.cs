using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>Stages password credentials on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfPasswordCredentials(IdentityDbContext db) : IPasswordCredentials
{
    public Task<PasswordCredential?> FindAsync(Guid principalId, CancellationToken cancellationToken) =>
        db.PasswordCredentials.SingleOrDefaultAsync(c => c.PrincipalId == principalId, cancellationToken);

    public void Add(PasswordCredential credential) => db.PasswordCredentials.Add(credential);
}
