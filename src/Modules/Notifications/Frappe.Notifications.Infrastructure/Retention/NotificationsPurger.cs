using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Infrastructure.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Infrastructure.ProviderEvents;
using Frappe.Notifications.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Notifications.Infrastructure.Retention;

/// <summary>
/// Deletes what Notifications keeps only for a while (S11): deliveries older than <see cref="NotificationsPurgeOptions.DeliveryDays"/> by
/// <c>created_at</c> and provider events older than <see cref="NotificationsPurgeOptions.ProviderEventDays"/> by
/// <c>received_at</c>, and send requests older than <see cref="NotificationsPurgeOptions.SendRequestDays"/> by
/// <c>requested_at</c>. Rows go in
/// batches of <see cref="NotificationsPurgeOptions.BatchSize"/>, each its own short statement, so the purge never holds many locks for long;
/// running it twice at once only makes the second find less. It counts the rows of each table on
/// <see cref="NotificationsMetrics"/>. Suppressions and preferences are never touched.
/// </summary>
internal sealed partial class NotificationsPurger(
    IServiceScopeFactory scopes,
    IOptions<NotificationsPurgeOptions> options,
    TimeProvider time,
    NotificationsMetrics metrics,
    ILogger<NotificationsPurger> logger)
{
    // Parameters: the cutoff and the batch; the table and its age column are constants. Deliveries are picked by their physical row identity.
    private const string DeliveriesSql = "DELETE FROM notifications.deliveries WHERE ctid IN "
        + "(SELECT ctid FROM notifications.deliveries WHERE created_at < {0} LIMIT {1})";

    private const string SendRequestsSql = "DELETE FROM notifications.send_requests WHERE ctid IN "
        + "(SELECT ctid FROM notifications.send_requests WHERE requested_at < {0} LIMIT {1})";

    // The Worker's role reads no key column of provider_events (S4: never the event id), so a batch is the oldest rows by received_at, up to the
    // received_at of the batch's last row: a tie at that instant may take a few rows more, never fewer, and the index on received_at serves it.
    private const string ProviderEventsSql = "DELETE FROM notifications.provider_events WHERE received_at < {0} AND received_at <= "
        + "COALESCE((SELECT received_at FROM notifications.provider_events WHERE received_at < {0} "
        + "ORDER BY received_at LIMIT 1 OFFSET {1} - 1), {0})";

    /// <summary>Purges the three tables and returns how many rows each lost.</summary>
    public async Task<(int Deliveries, int ProviderEvents, int SendRequests)> PurgeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var deliveries = await PurgeTableAsync(
            db, DeliveriesSql, DeliveryConfiguration.Table, now - Duration.FromDays(settings.DeliveryDays), settings.BatchSize, cancellationToken);
        var providerEvents = await PurgeTableAsync(
            db, ProviderEventsSql, ProviderEventConfiguration.Table, now - Duration.FromDays(settings.ProviderEventDays), settings.BatchSize,
            cancellationToken);
        var sendRequests = await PurgeTableAsync(
            db, SendRequestsSql, SendRequestConfiguration.Table, now - Duration.FromDays(settings.SendRequestDays), settings.BatchSize,
            cancellationToken);
        return (deliveries, providerEvents, sendRequests);
    }

    /// <summary>Runs the batch delete until a batch comes back short, then logs the rows.</summary>
    private async Task<int> PurgeTableAsync(
        DbContext db, string sql, string table, Instant cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlRawAsync(sql, [cutoff, batchSize], cancellationToken);
            total += deleted;
        }
        while (deleted >= batchSize);

        metrics.Purged(table, total);
        LogPurged(logger, total, table, cutoff);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Rows} rows from notifications.{Table} older than {Cutoff}")]
    private static partial void LogPurged(ILogger logger, int rows, string table, Instant cutoff);
}
