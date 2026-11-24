using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Identifiers;

/// <summary>Stages email identifiers on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfEmailIdentifiers(IdentityDbContext db) : IEmailIdentifiers
{
    public Task<EmailIdentifier?> FindByAddressAsync(string normalizedAddress, CancellationToken cancellationToken) =>
        db.EmailIdentifiers.SingleOrDefaultAsync(e => e.NormalizedAddress == normalizedAddress, cancellationToken);

    public Task<EmailIdentifier?> FindOfAsync(Guid principalId, CancellationToken cancellationToken) =>
        db.EmailIdentifiers.SingleOrDefaultAsync(e => e.PrincipalId == principalId, cancellationToken);

    public void Add(EmailIdentifier identifier) => db.EmailIdentifiers.Add(identifier);
}
