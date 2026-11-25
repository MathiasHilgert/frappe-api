using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// The sweep of ended sessions (S2, S16): ends every session that idled out, reached its absolute lifetime (the instant itself counts, as in
/// <see cref="SessionAccess"/>) or was revoked, so the purge has an <c>ended_at</c> to count from. Each batch is one transaction: a guarded
/// <c>UPDATE</c> (a session touched or ended since it was selected no longer matches and is skipped, never failing the sweep) and the
/// <see cref="SessionEnded"/> facts of exactly the rows it ended, raised through <see cref="Session.End"/> and written to the outbox by the
/// same save. The Worker's role holds no grant on the token, the address or the user agent, so the sessions are never loaded whole.
/// </summary>
internal sealed partial class SessionSweeper(
    IServiceScopeFactory scopes,
    IOptions<IdentityRetentionOptions> options,
    TimeProvider time,
    SessionsMetrics metrics,
    ILogger<SessionSweeper> logger) : IRetentionStep
{
    private const string Revoked = "revoked";

    // $1 is the instant of the sweep, $2 the batch. The inner select picks the batch; the outer predicate is checked again on the row version
    // the update locks, which is what skips a session a concurrent request touched meanwhile.
    private const string EndSql = """
        UPDATE identity.sessions SET ended_at = GREATEST($1, revoked_at)
        WHERE id IN (
            SELECT id FROM identity.sessions
            WHERE ended_at IS NULL AND (revoked_at IS NOT NULL OR idle_expires_at <= $1 OR absolute_expires_at <= $1)
            LIMIT $2)
          AND ended_at IS NULL AND (revoked_at IS NOT NULL OR idle_expires_at <= $1 OR absolute_expires_at <= $1)
        RETURNING id, principal_id, idle_expires_at, absolute_expires_at, revoked_at
        """;

    public async Task RunAsync(CancellationToken cancellationToken) => _ = await SweepAsync(cancellationToken);

    /// <summary>Ends the sessions that are due and returns how many it ended.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.BatchSize;
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var total = 0;
        int ended;
        do
        {
            ended = await SweepBatchAsync(now, batchSize, cancellationToken);
            total += ended;
        }
        while (ended >= batchSize);

        LogEnded(logger, total);
        return total;
    }

    private async Task<int> SweepBatchAsync(Instant now, int batchSize, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var counts = new Dictionary<string, int>();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var rows = await RetentionStatements.QueryAsync(
                db, EndSql, now, batchSize,
                row => (Id: row.GetGuid(0), PrincipalId: row.GetGuid(1), Idle: row.GetFieldValue<Instant>(2), Absolute: row.GetFieldValue<Instant>(3),
                    RevokedAt: row.IsDBNull(4) ? (Instant?)null : row.GetFieldValue<Instant>(4)),
                cancellationToken);
            foreach (var row in rows)
            {
                var session = Session.SeenBySweep(row.Id, row.PrincipalId, row.Idle, row.Absolute, row.RevokedAt);
                session.End(now);
                db.Sessions.Attach(session);
                var reason = session.DomainEvents.OfType<SessionEnded>().SingleOrDefault() is { } ended
                    ? StoredEnum<SessionEndReason>.ToText(ended.Reason)
                    : Revoked;
                counts[reason] = counts.GetValueOrDefault(reason) + 1;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var (reason, count) in counts)
        {
            metrics.SessionsEnded(reason, count);
        }

        return counts.Values.Sum();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ended {Sessions} sessions that expired or were revoked")]
    private static partial void LogEnded(ILogger logger, int sessions);
}
