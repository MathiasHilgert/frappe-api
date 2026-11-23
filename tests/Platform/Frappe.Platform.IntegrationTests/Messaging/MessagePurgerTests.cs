using Frappe.Messaging;
using Frappe.Messaging.Retention;
using Frappe.Platform.Testing.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// The purge deletes published outbox rows and inbox records past their retention, in batches, and nothing else:
/// unpublished and parked outbox rows are the only copy of an event the broker has not stored.
/// </summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class MessagePurgerTests(InboxDatabase database)
{
    private const string Module = "test";

    private const string OutboxColumns =
        "(id, type, source, data_content_type, data, occurred_at, published_at, attempts, next_attempt_at, last_error)";

    private static async Task ExecuteAsync(DbContextOptions<InboxTestContext> options, string sql)
    {
        await using var db = new InboxTestContext(options);
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task AddOutboxAsync(DbContextOptions<InboxTestContext> options, int count, string publishedAt, string attempts = "0") =>
        ExecuteAsync(
            options,
            $"""
            INSERT INTO module.outbox_messages {OutboxColumns}
            SELECT gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '1', now(),
                   {publishedAt}, {attempts}, NULL, CASE WHEN {attempts} > 0 THEN 'rejected' END
            FROM generate_series(1, {count})
            """);

    private static Task AddInboxAsync(DbContextOptions<InboxTestContext> options, int count, string receivedAt) =>
        ExecuteAsync(
            options,
            $"INSERT INTO module.inbox_messages (consumer, message_id, received_at) SELECT 'test-consumer', gen_random_uuid(), {receivedAt} "
            + $"FROM generate_series(1, {count})");

    private static async Task<(long Outbox, long Inbox, long OutboxOldPublished)> CountsAsync(DbContextOptions<InboxTestContext> options)
    {
        await using var db = new InboxTestContext(options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM module.outbox_messages), (SELECT count(*) FROM module.inbox_messages), "
            + "(SELECT count(*) FROM module.outbox_messages WHERE published_at < now() - interval '7 days')",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static MessagePurger<InboxTestContext> PurgerFor(
        DbContextOptions<InboxTestContext> options, MessageRetentionOptions retention, out ServiceProvider services)
    {
        services = new ServiceCollection().AddMetrics().AddScoped(_ => new InboxTestContext(options)).BuildServiceProvider();
        return new MessagePurger<InboxTestContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            Module,
            TimeProvider.System,
            retention,
            new MessagingMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), TimeProvider.System),
            NullLogger<MessagePurger<InboxTestContext>>.Instance);
    }

    [Test]
    public async Task Only_old_published_outbox_rows_and_old_inbox_records_are_deleted()
    {
        // Arrange
        var options = await database.CreateAsync();
        await AddOutboxAsync(options, 3, "now() - interval '8 days'");
        await AddOutboxAsync(options, 2, "now() - interval '1 day'");
        await AddOutboxAsync(options, 2, "NULL");
        await AddOutboxAsync(options, 1, "NULL", attempts: "10");
        await AddInboxAsync(options, 4, "now() - interval '15 days'");
        await AddInboxAsync(options, 2, "now() - interval '1 day'");
        var purger = PurgerFor(options, new MessageRetentionOptions(), out var services);
        await using var _ = services;

        // Act
        var result = await purger.PurgeAsync(CancellationToken.None);

        // Assert
        var (outbox, inbox, oldPublished) = await CountsAsync(options);
        using (Assert.Multiple())
        {
            await Assert.That(result).IsEqualTo(new MessagePurgeResult(3, 4));
            await Assert.That(outbox).IsEqualTo(5);
            await Assert.That(inbox).IsEqualTo(2);
            await Assert.That(oldPublished).IsEqualTo(0);
            await Assert.That(purger.Module).IsEqualTo(Module);
        }
    }

    [Test]
    public async Task A_batch_size_smaller_than_the_rows_to_delete_still_drains_everything()
    {
        // Arrange
        var options = await database.CreateAsync();
        await AddOutboxAsync(options, 7, "now() - interval '30 days'");
        await AddInboxAsync(options, 5, "now() - interval '30 days'");
        await AddOutboxAsync(options, 1, "NULL");
        var purger = PurgerFor(options, new MessageRetentionOptions { BatchSize = 2 }, out var services);
        await using var _ = services;

        // Act
        var result = await purger.PurgeAsync(CancellationToken.None);

        // Assert
        var (outbox, inbox, _) = await CountsAsync(options);
        using (Assert.Multiple())
        {
            await Assert.That(result).IsEqualTo(new MessagePurgeResult(7, 5));
            await Assert.That(outbox).IsEqualTo(1);
            await Assert.That(inbox).IsEqualTo(0);
            await Assert.That(purger.Options.BatchSize).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_purge_with_nothing_to_delete_reports_zero_rows()
    {
        // Arrange
        var options = await database.CreateAsync();
        var purger = PurgerFor(options, new MessageRetentionOptions(), out var services);
        await using var _ = services;

        // Act
        var result = await purger.PurgeAsync(CancellationToken.None);

        // Assert
        await Assert.That(result).IsEqualTo(new MessagePurgeResult(0, 0));
    }

    [Test]
    public async Task A_context_without_the_outbox_cannot_be_purged_and_the_error_names_the_registration()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NoMessagingContext>().UseNpgsql("Host=unused;Database=unused").Options;
        using var services = new ServiceCollection().AddMetrics().AddScoped(_ => new NoMessagingContext(options)).BuildServiceProvider();
        var purger = new MessagePurger<NoMessagingContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            Module,
            TimeProvider.System,
            new MessageRetentionOptions(),
            new MessagingMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), TimeProvider.System),
            NullLogger<MessagePurger<NoMessagingContext>>.Instance);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => purger.PurgeAsync(CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("AddOutbox");
    }

    [Test]
    public async Task A_failing_purge_is_counted_by_module_and_still_throws()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<NoMessagingContext>().UseNpgsql("Host=unused;Database=unused").Options;
        using var services = new ServiceCollection().AddMetrics().AddScoped(_ => new NoMessagingContext(options)).BuildServiceProvider();
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        using var capture = new Frappe.Testing.MetricCapture(factory);
        var purger = new MessagePurger<NoMessagingContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            Module,
            TimeProvider.System,
            new MessageRetentionOptions(),
            new MessagingMetrics(factory, TimeProvider.System),
            NullLogger<MessagePurger<NoMessagingContext>>.Instance);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => purger.PurgeAsync(CancellationToken.None));
        var counted = capture.Of(MessagingMetrics.PurgeFailuresMetric).Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(counted.Value).IsEqualTo(1d);
            await Assert.That(counted.Tags[MessagingMetrics.ModuleTag]).IsEqualTo(Module);
        }
    }

    /// <summary>A context that forgot to map the outbox and the inbox.</summary>
    private sealed class NoMessagingContext(DbContextOptions<NoMessagingContext> options) : DbContext(options);
}
