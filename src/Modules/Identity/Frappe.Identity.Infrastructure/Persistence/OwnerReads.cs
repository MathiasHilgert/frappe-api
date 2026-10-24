using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Persistence;

// Public so Wolverine builds it inline. Sessions are filtered in SQL with the aggregate's own windows (Session.IsLive).
public sealed class OwnerReads(IdentityDbContext context) : IOwnerReads
{
    public async Task<OwnerProfile?> GetProfileAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var row = await context.Principals.AsNoTracking()
            .Where(p => p.Id == principalId)
            .Select(p => new { p.Id, p.Owner.Name, p.Owner.Email, p.Owner.Status, p.Owner.Locale })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new OwnerProfile(row.Id, row.Name.Value, row.Email.Value, row.Status.ToString().ToLowerInvariant(), row.Locale.Value);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListLiveSessionsAsync(
        Guid principalId, Guid currentSessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await LiveSessions(principalId, now)
            .OrderByDescending(s => s.LastSeenAt)
            .ThenBy(s => s.Id)
            .Select(s => new SessionSummary(s.Id, s.CreatedAt, s.LastSeenAt, s.Id == currentSessionId))
            .ToListAsync(cancellationToken);

    public Task<bool> HasFreshStepUpAsync(Guid principalId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        LiveSessions(principalId, now).AnyAsync(s => s.Id == sessionId && s.StepUpExpiresAt > now, cancellationToken);

    // now < LastSeenAt + IdleTimeout && now < CreatedAt + AbsoluteLifetime, rewritten so the columns stay bare.
    private IQueryable<Session> LiveSessions(Guid principalId, DateTimeOffset now)
    {
        var idleCutoff = now - Session.IdleTimeout;
        var absoluteCutoff = now - Session.AbsoluteLifetime;
        return context.Principals.AsNoTracking()
            .Where(p => p.Id == principalId)
            .SelectMany(p => p.Sessions)
            .Where(s => s.LastSeenAt > idleCutoff && s.CreatedAt > absoluteCutoff);
    }
}
