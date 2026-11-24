using Frappe.Identity.Application.Codes;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>Stages undo tokens on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfEmailChangeUndos(IdentityDbContext db) : IEmailChangeUndos
{
    public Task<EmailChangeUndo?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.EmailChangeUndos.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<EmailChangeUndo?> LiveOfPairAsync(Guid principalId, string oldAddress, string newAddress, CancellationToken cancellationToken) =>
        db.EmailChangeUndos.SingleOrDefaultAsync(
            u => u.PrincipalId == principalId && u.OldAddress == oldAddress && u.NewAddress == newAddress
                && u.RedeemedAt == null && u.InvalidatedAt == null,
            cancellationToken);

    public async Task<IReadOnlyList<EmailChangeUndo>> LiveIssuedAfterAsync(Guid principalId, Instant instant, CancellationToken cancellationToken) =>
        await db.EmailChangeUndos.Where(u => u.PrincipalId == principalId && u.IssuedAt >= instant && u.RedeemedAt == null && u.InvalidatedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(EmailChangeUndo undo) => db.EmailChangeUndos.Add(undo);
}
