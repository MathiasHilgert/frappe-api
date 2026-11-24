using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Reads sessions untracked, straight into the views the queries answer with (CQRS-08), one statement each.</summary>
internal sealed class EfSessionReads(IdentityDbContext db) : ISessionReads
{
    public async Task<SessionAccess?> FindAccessAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        var row = await db.Sessions.AsNoTracking()
            .Where(s => s.TokenHash == tokenHash)
            .Join(db.Principals, s => s.PrincipalId, p => p.Id, (s, p) => new
            {
                s.Id,
                s.PrincipalId,
                p.Kind,
                p.Status,
                PrincipalStamp = p.CredentialStamp,
                SessionStamp = s.CredentialStamp,
                s.Methods,
                s.LastSeenAt,
                s.IdleExpiresAt,
                s.AbsoluteExpiresAt,
                s.EndedAt,
                s.RevokedAt,
                s.StepUpUntil,
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new SessionAccess(
                row.Id, row.PrincipalId, row.Kind, row.Status, row.PrincipalStamp, row.SessionStamp, row.Methods, row.LastSeenAt,
                row.IdleExpiresAt, row.AbsoluteExpiresAt, row.EndedAt, row.RevokedAt, row.StepUpUntil);
    }

    public Task<CurrentSession?> FindCurrentAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sessions.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new CurrentSession(s.Id, s.IdleExpiresAt, s.AbsoluteExpiresAt, s.StepUpUntil))
            .SingleOrDefaultAsync(cancellationToken);
}
