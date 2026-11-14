using Frappe.Messaging.Inbox;
using Frappe.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Frappe.Messaging.Retention;

/// <summary>
/// Deletes what a module no longer needs from its own schema: published outbox rows older than
/// <see cref="MessageRetentionOptions.OutboxRetention"/> and inbox records older than
/// <see cref="MessageRetentionOptions.InboxRetention"/>. Unpublished outbox rows, parked ones included, are never
/// touched. Rows go in batches of <see cref="MessageRetentionOptions.BatchSize"/>, each its own short statement, so the
/// purge never holds many locks for long; running it twice at once only makes the second one find less.
/// </summary>
public sealed partial class MessagePurger<TContext>(
    IServiceScopeFactory scopes,
    string module,
    TimeProvider time,
    MessageRetentionOptions options,
    MessagingMetrics metrics,
    ILogger<MessagePurger<TContext>> logger)
    where TContext : DbContext
{
    /// <summary>The module whose tables this purger cleans, for example <c>geo</c>.</summary>
    public string Module { get; } = module;

    public MessageRetentionOptions Options { get; } = options;

    /// <summary>Purges both tables and returns how many rows each lost.</summary>
    public async Task<MessagePurgeResult> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());

        var outbox = TableOf<OutboxMessage>(db, "AddOutbox");
        var outboxRows = await PurgeTableAsync(
            db,
            outbox,
            "DELETE FROM " + outbox.Name + " WHERE id IN (SELECT id FROM " + outbox.Name
                + " WHERE published_at IS NOT NULL AND published_at < {0} LIMIT {1})",
            now - Duration.FromTimeSpan(Options.OutboxRetention),
            cancellationToken);

        var inbox = TableOf<InboxMessage>(db, "AddInbox");
        var inboxRows = await PurgeTableAsync(
            db,
            inbox,
            "DELETE FROM " + inbox.Name + " WHERE (consumer, message_id) IN (SELECT consumer, message_id FROM " + inbox.Name
                + " WHERE received_at < {0} LIMIT {1})",
            now - Duration.FromTimeSpan(Options.InboxRetention),
            cancellationToken);

        return new MessagePurgeResult(outboxRows, inboxRows);
    }

    /// <summary>Runs the batch delete until a batch comes back short, then logs and counts the rows.</summary>
    private async Task<int> PurgeTableAsync(DbContext db, (string Name, string TableName) table, string sql, Instant cutoff, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlRawAsync(sql, [cutoff, Options.BatchSize], cancellationToken);
            total += deleted;
        }
        while (deleted >= Options.BatchSize);

        metrics.Purged(Module, table.TableName, total);
        LogPurged(logger, total, table.Name, Module, cutoff);
        return total;
    }

    /// <summary>A table's quoted, schema-qualified name for SQL and its bare name for telemetry.</summary>
    private static (string Name, string TableName) TableOf<TEntity>(DbContext db, string registration)
    {
        var entity = db.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no {typeof(TEntity).Name}; call modelBuilder.{registration}().");
        var tableName = entity.GetTableName()!;
        return ($"\"{entity.GetSchema()}\".\"{tableName}\"", tableName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Rows} rows from {Table} of module {Module} older than {Cutoff}")]
    private static partial void LogPurged(ILogger logger, int rows, string table, string module, Instant cutoff);
}
