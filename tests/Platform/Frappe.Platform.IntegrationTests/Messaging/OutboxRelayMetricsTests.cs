using System.Data.Common;
using Frappe.Messaging;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Platform.Testing.Messaging;
using Frappe.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// The relay counts what it published and reports the age of the oldest message still waiting in its module's outbox,
/// including one it cannot take yet, against real Postgres and NATS.
/// </summary>
[ClassDataSource<InboxDatabase, NatsServer>(Shared = [SharedType.PerTestSession, SharedType.PerTestSession])]
public sealed class OutboxRelayMetricsTests(InboxDatabase database, NatsServer nats)
{
    private const string Module = "test";

    private static readonly DateTimeOffset Occurred = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static async Task InsertAsync(DbContextOptions<InboxTestContext> options, string values)
    {
        await using var db = new InboxTestContext(options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO module.outbox_messages (id, type, source, data_content_type, data, occurred_at, attempts, next_attempt_at) VALUES "
            + values,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(ServiceProvider Services, OutboxRelay<InboxTestContext> Relay, MetricCapture Capture)> RelayAsync(
        DbContextOptions<InboxTestContext> options, TimeProvider time, MessagingMetrics? metrics = null)
    {
        var naming = NatsServer.FreshNaming();
        await JetStreamTopology.EnsureModuleStreamAsync(nats.JetStream, naming, Module, CancellationToken.None);
        var services = new ServiceCollection().AddMetrics().AddScoped(_ => new InboxTestContext(options)).BuildServiceProvider();
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        var capture = new MetricCapture(factory);
        var relay = new OutboxRelay<InboxTestContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            new JetStreamPublisher(nats.JetStream, naming),
            time,
            new OutboxRelayOptions(),
            Module,
            metrics ?? new MessagingMetrics(factory, time),
            NullLogger<OutboxRelay<InboxTestContext>>.Instance);
        return (services, relay, capture);
    }

    [Test]
    public async Task A_pass_counts_the_messages_it_published_and_then_reports_nothing_waiting()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, """
            (gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{"what":"a"}', now(), 0, NULL),
            (gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{"what":"b"}', now(), 0, NULL)
            """);
        var (services, relay, capture) = await RelayAsync(options, TimeProvider.System);
        await using var _ = services;
        using var __ = capture;

        // Act
        var published = await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();

        // Assert
        var relayed = capture.Of(MessagingMetrics.RelayedMetric).Single();
        var age = capture.Of(MessagingMetrics.OutboxOldestAgeMetric).Single();
        using (Assert.Multiple())
        {
            await Assert.That(published).IsEqualTo(2);
            await Assert.That(relayed.Value).IsEqualTo(2d);
            await Assert.That(relayed.Tags[MessagingMetrics.ModuleTag]).IsEqualTo(Module);
            await Assert.That(age.Value).IsEqualTo(0d);
            await Assert.That(age.Tags[MessagingMetrics.ModuleTag]).IsEqualTo(Module);
        }
    }

    [Test]
    public async Task A_message_waiting_out_its_backoff_or_parked_still_ages_the_outbox_although_the_pass_cannot_take_it()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, $$"""
            (gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{}',
             '{{Occurred:O}}', 3, '{{Occurred.AddDays(1):O}}'),
            (gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{}',
             '{{Occurred.AddMinutes(1):O}}', 10, NULL)
            """);
        var time = new FakeTimeProvider(Occurred.AddMinutes(10));
        var (services, relay, capture) = await RelayAsync(options, time);
        await using var _ = services;
        using var __ = capture;

        var published = await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();
        time.SetUtcNow(Occurred.AddMinutes(15));

        // Act
        capture.Observe();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(published).IsEqualTo(0);
            await Assert.That(capture.Of(MessagingMetrics.RelayedMetric)).IsEmpty();
            await Assert.That(capture.Of(MessagingMetrics.OutboxOldestAgeMetric).Select(m => m.Value)).IsEquivalentTo([600d, 900d]);
        }
    }

    [Test]
    public async Task A_failing_oldest_lookup_keeps_the_last_age_and_never_fails_a_pass_that_published()
    {
        // Arrange
        var options = await database.CreateAsync();
        var time = new FakeTimeProvider(Occurred.AddMinutes(10));
        await using var meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        using var capture = new MetricCapture(factory);
        var metrics = new MessagingMetrics(factory, time);
        metrics.OldestUnpublished(Module, NodaTime.Instant.FromDateTimeOffset(Occurred));
        await InsertAsync(options, """
            (gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{"what":"a"}', now(), 0, NULL)
            """);
        var failingSelects = new DbContextOptionsBuilder<InboxTestContext>(options).AddInterceptors(new FailingSelects()).Options;
        var (services, relay, relayCapture) = await RelayAsync(failingSelects, time, metrics);
        await using var _ = services;
        using var __ = relayCapture;

        // Act
        var published = await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();
        await using var db = new InboxTestContext(options);
        var unpublished = await db.Set<OutboxMessage>().CountAsync(m => m.PublishedAt == null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(published).IsEqualTo(1);
            await Assert.That(capture.Of(MessagingMetrics.RelayedMetric).Single().Value).IsEqualTo(1d);
            await Assert.That(capture.Of(MessagingMetrics.OutboxOldestAgeMetric).Single().Value).IsEqualTo(600d);
            await Assert.That(unpublished).IsEqualTo(0);
        }
    }

    /// <summary>
    /// Fails every query that reads: the relay's claim and settle are <c>UPDATE</c>s, so only its oldest lookup is hit,
    /// as if the database failed right after the pass published.
    /// </summary>
    private sealed class FailingSelects : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("The database failed the lookup.")
                : ValueTask.FromResult(result);
    }
}
