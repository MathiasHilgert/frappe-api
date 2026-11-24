using Frappe.Notifications.Infrastructure.Retention;
using Frappe.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The nightly purge against a migrated database, as the Worker's role (S11): deliveries and provider events older than their window go in
/// batches, the younger ones stay, suppressions and preferences never go, and the job also purges the module's messages.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class PurgeTests(PostgresServer server)
{
    private static readonly Dictionary<string, string?> SmallBatches = new() { ["Notifications:Purge:BatchSize"] = "2" };

    private static DispatchWorld Worker(string owner, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null) =>
        DispatchWorld.ForWorker(owner, settings ?? SmallBatches, configure);

    private static Task AddDeliveriesAsync(string owner, int count, int daysOld) => ScratchDatabase.ExecuteAsync(owner, $"""
        INSERT INTO notifications.deliveries (id, source_message_id, kind, category, channel, recipient_key, status, created_at, status_changed_at)
        SELECT gen_random_uuid(), gen_random_uuid(), 'account_activation_code', 'security', 'email', 'ref:principal:' || n, 'unresolved',
          now() - interval '{daysOld} days', now() - interval '{daysOld} days'
        FROM generate_series(1, {count}) AS n
        """);

    private static Task AddEventsAsync(string owner, string prefix, int count, int daysOld) => ScratchDatabase.ExecuteAsync(owner, $"""
        INSERT INTO notifications.provider_events (provider, event_id, received_at, matched)
        SELECT 'resend', '{prefix}_' || n, now() - interval '{daysOld} days' - n * interval '1 second', true FROM generate_series(1, {count}) AS n
        """);

    private static Task AddRequestsAsync(string owner, int count, int daysOld) => ScratchDatabase.ExecuteAsync(owner, $"""
        INSERT INTO notifications.send_requests (id, kind, requested_at)
        SELECT gen_random_uuid(), 'account_activation_code', now() - interval '{daysOld} days' FROM generate_series(1, {count})
        """);

    private static async Task<long> CountAsync(string owner, string table, string? where = null) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM notifications.{table} {where}"))!;

    [Test]
    public async Task Deliveries_older_than_ninety_days_and_events_older_than_seven_go_in_batches_and_younger_rows_stay()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: five old and three young rows each, with a batch of two, so the old ones take three statements.
            await using var worker = Worker(owner);
            await AddDeliveriesAsync(owner, 5, daysOld: 91);
            await AddDeliveriesAsync(owner, 3, daysOld: 89);
            await AddEventsAsync(owner, "old", 5, daysOld: 8);
            await AddEventsAsync(owner, "young", 3, daysOld: 6);
            await AddRequestsAsync(owner, 5, daysOld: 91);
            await AddRequestsAsync(owner, 3, daysOld: 89);

            // Act
            var (deliveries, events, requests) = await worker.Services.GetRequiredService<NotificationsPurger>().PurgeAsync(CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(deliveries).IsEqualTo(5);
                await Assert.That(events).IsEqualTo(5);
                await Assert.That(requests).IsEqualTo(5);
                await Assert.That(await CountAsync(owner, "send_requests")).IsEqualTo(3);
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(3);
                await Assert.That(await CountAsync(owner, "deliveries", "WHERE created_at < now() - interval '90 days'")).IsEqualTo(0);
                await Assert.That(await CountAsync(owner, "provider_events")).IsEqualTo(3);
                await Assert.That(await CountAsync(owner, "provider_events", "WHERE event_id LIKE 'young_%'")).IsEqualTo(3);
            }
        });
    }

    [Test]
    public async Task A_configured_retention_moves_the_window_and_suppressions_and_preferences_never_expire()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var settings = new Dictionary<string, string?>
            {
                ["Notifications:Purge:DeliveryDays"] = "30",
                ["Notifications:Purge:ProviderEventDays"] = "2",
            };
            await using var worker = Worker(owner, settings);
            await AddDeliveriesAsync(owner, 2, daysOld: 40);
            await AddDeliveriesAsync(owner, 2, daysOld: 20);
            await AddEventsAsync(owner, "old", 2, daysOld: 3);
            await AddEventsAsync(owner, "young", 2, daysOld: 1);
            await ScratchDatabase.ExecuteAsync(owner, $"""
                INSERT INTO notifications.suppressions (channel, address_hash, hash_key_id, reason, source, created_at)
                VALUES ('email', '\x{new string('a', 64)}'::bytea, 1, 'complaint', 'webhook', now() - interval '1000 days');
                INSERT INTO notifications.preferences (recipient_key, category, opted_out_at)
                VALUES ('ref:principal:42', 'marketing', now() - interval '1000 days')
                """);

            // Act
            await worker.Services.GetRequiredService<NotificationsPurger>().PurgeAsync(CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(2);
                await Assert.That(await CountAsync(owner, "provider_events", "WHERE event_id LIKE 'young_%'")).IsEqualTo(2);
                await Assert.That(await CountAsync(owner, "provider_events")).IsEqualTo(2);
                await Assert.That(await CountAsync(owner, "suppressions")).IsEqualTo(1);
                await Assert.That(await CountAsync(owner, "preferences")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task The_job_purges_the_rows_and_then_the_modules_messages()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var messages = new RecordingMessagePurge();
            await using var worker = Worker(owner, configure: services => services.AddSingleton<IMessagePurge>(messages));
            await AddDeliveriesAsync(owner, 3, daysOld: 100);
            var job = ActivatorUtilities.CreateInstance<NotificationsPurgeJob>(worker.Services);

            // Act
            await job.Execute(null!, CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(0);
                await Assert.That(messages.Runs).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_failing_message_purge_does_not_skip_the_rows_and_fails_the_job()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var messages = new RecordingMessagePurge { Fails = true };
            await using var worker = Worker(owner, configure: services => services.AddSingleton<IMessagePurge>(messages));
            await AddDeliveriesAsync(owner, 3, daysOld: 100);
            var job = ActivatorUtilities.CreateInstance<NotificationsPurgeJob>(worker.Services);

            // Act
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await job.Execute(null!, CancellationToken.None));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(thrown!.Message).Contains("messages");
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(0);
            }
        });
    }

    /// <summary>Counts the runs of the message purge and fails on demand.</summary>
    private sealed class RecordingMessagePurge : IMessagePurge
    {
        public int Runs { get; private set; }

        public bool Fails { get; init; }

        public Task PurgeAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return Fails ? Task.FromException(new InvalidOperationException("messages")) : Task.CompletedTask;
        }
    }
}
