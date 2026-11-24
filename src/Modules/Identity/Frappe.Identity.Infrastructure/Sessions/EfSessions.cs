using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Stages sessions on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfSessions(IdentityDbContext db) : ISessions
{
    public Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sessions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Session>> ListLiveOfAsync(Guid principalId, CancellationToken cancellationToken) =>
        await db.Sessions.Where(s => s.PrincipalId == principalId && s.RevokedAt == null && s.EndedAt == null).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<Session>> ListLiveOfAsync(Guid principalId, Instant now, CancellationToken cancellationToken) =>
        await db.Sessions
            .Where(s => s.PrincipalId == principalId && s.RevokedAt == null && s.EndedAt == null && s.IdleExpiresAt > now
                && s.AbsoluteExpiresAt > now)
            .ToListAsync(cancellationToken);

    public void Add(Session session) => db.Sessions.Add(session);
}
