using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// Deletes what the core keeps only for a while (S16): ended sessions (and their truncated address) older than
/// <see cref="IdentityRetentionOptions.EndedSessionDays"/>, and codes expired for longer than <see cref="IdentityRetentionOptions.CodeDays"/>.
/// Rows go in batches of <see cref="IdentityRetentionOptions.BatchSize"/>, each its own short statement. Counted on
/// <see cref="IdentityMetrics"/> once committed. The pending owners are the Owners feature's own step.
/// </summary>
internal sealed partial class StaleRowsPurger(
    IServiceScopeFactory scopes,
    IOptions<IdentityRetentionOptions> options,
    TimeProvider time,
    IdentityMetrics metrics,
    ILogger<StaleRowsPurger> logger) : IRetentionStep
{
    private const string SessionsTarget = "sessions";
    private const string CodesTarget = "codes";

    // $1 is the cutoff, $2 the batch.
    private const string SessionsSql = """
        DELETE FROM identity.sessions
        WHERE id IN (SELECT id FROM identity.sessions WHERE ended_at < $1 ORDER BY ended_at LIMIT $2)
        RETURNING id
        """;

    private const string CodesSql = """
        DELETE FROM identity.one_time_codes
        WHERE id IN (SELECT id FROM identity.one_time_codes WHERE expires_at < $1 ORDER BY expires_at LIMIT $2)
        RETURNING id
        """;

    public async Task RunAsync(CancellationToken cancellationToken) => _ = await PurgeAsync(cancellationToken);

    /// <summary>Purges both targets and returns how many rows each lost.</summary>
    public async Task<(int Sessions, int Codes)> PurgeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var sessions = await PurgeAsync(
            SessionsTarget, SessionsSql, now - Duration.FromDays(settings.EndedSessionDays), settings.BatchSize, cancellationToken);
        var codes = await PurgeAsync(CodesTarget, CodesSql, now - Duration.FromDays(settings.CodeDays), settings.BatchSize, cancellationToken);
        return (sessions, codes);
    }

    private async Task<int> PurgeAsync(string target, string sql, Instant cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            deleted = (await RetentionStatements.QueryAsync(db, sql, cutoff, batchSize, row => row.GetGuid(0), cancellationToken)).Count;
            metrics.Purged(target, deleted);
            total += deleted;
        }
        while (deleted >= batchSize);

        LogPurged(logger, total, target, cutoff);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Rows} {Target} older than {Cutoff}")]
    private static partial void LogPurged(ILogger logger, int rows, string target, Instant cutoff);
}
