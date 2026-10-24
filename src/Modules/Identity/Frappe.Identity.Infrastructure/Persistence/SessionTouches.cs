using Frappe.Identity.Application.Sessions;
using Frappe.Modularity;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>
/// One conditional <c>UPDATE</c> on the sessions table. It bypasses the aggregate on purpose: a touch only moves an
/// expiry, and loading the principal to save it would bump its concurrency version and race every other write.
/// </summary>
[InlinedByWolverine]
public sealed class SessionTouches(IdentityDbContext context) : ISessionTouches
{
    public async Task<bool> TouchAsync(
        Guid sessionId, DateTimeOffset now, DateTimeOffset lastSeenBefore, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlAsync(
            $"UPDATE identity.sessions SET last_seen_at = {now} WHERE id = {sessionId} AND last_seen_at < {lastSeenBefore}",
            cancellationToken) == 1;
}
