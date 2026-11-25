using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>
/// Deletes the owners that never verified their address, with all their data, once <see cref="IdentityRetentionOptions.PendingOwnerDays"/> have
/// passed since their registration (S16). Each batch is one transaction: the delete and the <see cref="OwnerPendingPurged"/> facts of exactly
/// the owners it removed, raised through <see cref="Owner.PurgedPending"/>, committed together.
/// </summary>
internal sealed partial class PendingOwnerPurger(
    IServiceScopeFactory scopes,
    IOptions<IdentityRetentionOptions> options,
    TimeProvider time,
    RetentionMetrics metrics,
    ILogger<PendingOwnerPurger> logger) : IRetentionStep
{
    private const string Target = "owners";

    // $1 is the cutoff, $2 the batch. The principal row is what is deleted; the owner and everything else of it go by cascade. Its status is
    // checked on the row the delete locks, so an owner verified while the batch was selected (the verification activates the principal in the
    // same transaction) no longer matches and is skipped, with no fact. The principal goes first, as it does in the verification, so the two
    // never wait for each other in turns.
    private const string Sql = """
        DELETE FROM identity.principals
        WHERE status = 'pending'
          AND id IN (
            SELECT principal_id FROM identity.owners WHERE verified_at IS NULL AND registered_at < $1 ORDER BY registered_at LIMIT $2)
        RETURNING id
        """;

    public async Task RunAsync(CancellationToken cancellationToken) => _ = await PurgeAsync(cancellationToken);

    /// <summary>Purges the pending owners past the window and returns how many it removed.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var cutoff = now - Duration.FromDays(settings.PendingOwnerDays);
        var total = 0;
        int deleted;
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                var ids = await RetentionStatements.QueryAsync(db, Sql, cutoff, settings.BatchSize, row => row.GetGuid(0), cancellationToken);
                foreach (var id in ids)
                {
                    db.Owners.Attach(Owner.PurgedPending(id, now));
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                deleted = ids.Count;
            }

            metrics.Purged(Target, deleted);
            total += deleted;
        }
        while (deleted >= settings.BatchSize);

        LogPurged(logger, total, cutoff);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Rows} pending owners registered before {Cutoff}")]
    private static partial void LogPurged(ILogger logger, int rows, Instant cutoff);
}
