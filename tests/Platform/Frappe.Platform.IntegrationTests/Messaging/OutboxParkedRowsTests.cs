using System.Collections.Concurrent;
using System.Data.Common;
using Frappe.Messaging;
using Frappe.Messaging.Outbox;
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
/// Parked outbox rows against real Postgres: the relay's claim never walks them, an idle outbox is not looked up on
/// every pass, and an operator still sees how many rows are parked and can requeue one.
/// </summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class OutboxParkedRowsTests(InboxDatabase database)
{
    private const string Module = "test";

    private static readonly DateTimeOffset Occurred = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static async Task ExecuteAsync(DbContextOptions<InboxTestContext> options, string sql)
    {
        await using var db = new InboxTestContext(options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts <paramref name="count"/> rows with these attempts and next attempt time (SQL literals).</summary>
    private static Task InsertAsync(DbContextOptions<InboxTestContext> options, int count, int attempts, string nextAttemptAt) =>
        ExecuteAsync(options, $"""
            INSERT INTO module.outbox_messages (id, type, source, data_content_type, data, occurred_at, attempts, next_attempt_at)
            SELECT gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{"{}"}',
                '{Occurred:O}'::timestamptz + make_interval(secs => n), {attempts}, {nextAttemptAt}
            FROM generate_series(1, {count}) AS n
            """);

    private static (ServiceProvider Services, OutboxRelay<InboxTestContext> Relay, MetricCapture Capture) Relay(
        DbContextOptions<InboxTestContext> options, TimeProvider time, IMessagePublisher publisher, OutboxRelayOptions? relayOptions = null)
    {
        var services = new ServiceCollection().AddMetrics().AddScoped(_ => new InboxTestContext(options)).BuildServiceProvider();
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        var capture = new MetricCapture(factory);
        var relay = new OutboxRelay<InboxTestContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            time,
            relayOptions ?? new OutboxRelayOptions(),
            Module,
            new MessagingMetrics(factory, time),
            NullLogger<OutboxRelay<InboxTestContext>>.Instance);
        return (services, relay, capture);
    }

    [Test]
    public async Task The_claim_is_served_by_the_pending_index_and_never_visits_a_parked_row()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, 200, 10, "NULL");
        var commands = new CommandLog();
        var logged = new DbContextOptionsBuilder<InboxTestContext>(options).AddInterceptors(commands).Options;
        var (services, relay, capture) = Relay(logged, TimeProvider.System, new Accepting());
        await using var _ = services;
        using var __ = capture;

        var published = await relay.PublishPendingAsync(CancellationToken.None);
        var claim = commands.Commands.First(c => c.Text.TrimStart().StartsWith("UPDATE", StringComparison.Ordinal));

        // Act
        var plan = await AnalyzeWithoutSequentialScansAsync(options, claim);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(published).IsEqualTo(0);
            await Assert.That(plan).Contains(OutboxModel.PendingIndex);
            await Assert.That(plan).DoesNotContain("Rows Removed by Filter");
        }
    }

    [Test]
    public async Task An_idle_outbox_is_looked_up_once_per_sample_interval_not_on_every_pass()
    {
        // Arrange
        var options = await database.CreateAsync();
        var commands = new CommandLog();
        var logged = new DbContextOptionsBuilder<InboxTestContext>(options).AddInterceptors(commands).Options;
        var time = new FakeTimeProvider(Occurred);
        var relayOptions = new OutboxRelayOptions();
        var (services, relay, capture) = Relay(logged, time, new Accepting(), relayOptions);
        await using var _ = services;
        using var __ = capture;

        for (var pass = 0; pass < 3; pass++)
        {
            await relay.PublishPendingAsync(CancellationToken.None);
        }

        var withinInterval = commands.Selects;
        time.SetUtcNow(Occurred + relayOptions.SampleInterval);
        await relay.PublishPendingAsync(CancellationToken.None);

        // Act
        capture.Observe();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(withinInterval).IsEqualTo(1);
            await Assert.That(commands.Selects).IsEqualTo(2);
            await Assert.That(capture.Of(MessagingMetrics.OutboxOldestAgeMetric).Single().Value).IsEqualTo(0d);
        }
    }

    [Test]
    public async Task Parked_rows_are_counted_by_module_and_a_requeued_one_goes_out_on_the_next_pass()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, 2, 10, "NULL");
        await InsertAsync(options, 1, 3, $"'{Occurred.AddDays(1):O}'");
        var time = new FakeTimeProvider(Occurred.AddMinutes(10));
        var (services, relay, capture) = Relay(options, time, new Accepting());
        await using var _ = services;
        using var __ = capture;

        var before = await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();
        // The requeue OutboxRelayOptions.MaxAttempts documents for operators: a fresh set of attempts, due now.
        await ExecuteAsync(options, """
            UPDATE module.outbox_messages SET attempts = 0, next_attempt_at = NULL
            WHERE id = (
              SELECT id FROM module.outbox_messages
              WHERE published_at IS NULL AND attempts > 0 AND next_attempt_at IS NULL ORDER BY sequence LIMIT 1)
            """);

        // The parked count is sampled once per SampleInterval during a drain (S18), so the next lookup is an interval later.
        time.Advance(new OutboxRelayOptions().SampleInterval + TimeSpan.FromSeconds(1));

        // Act
        var requeued = await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();

        // Assert
        var parked = capture.Of(MessagingMetrics.OutboxParkedMetric);
        using (Assert.Multiple())
        {
            await Assert.That(before).IsEqualTo(0);
            await Assert.That(requeued).IsEqualTo(1);
            await Assert.That(parked.Select(m => m.Value)).IsEquivalentTo([2d, 1d]);
            await Assert.That(parked.All(m => (string?)m.Tags[MessagingMetrics.ModuleTag] == Module)).IsTrue();
        }
    }

    [Test]
    public async Task A_message_rejected_for_the_last_time_leaves_the_pending_rows_and_is_counted_as_parked()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, 1, 0, "NULL");
        var time = new FakeTimeProvider(Occurred.AddMinutes(10));
        var (services, relay, capture) = Relay(options, time, new Rejecting(), new OutboxRelayOptions { MaxAttempts = 1 });
        await using var _ = services;
        using var __ = capture;

        await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();

        await using var db = new InboxTestContext(options);

        // Act
        var pending = await db.Database.SqlQueryRaw<long>(
            $"SELECT count(*) AS \"Value\" FROM module.outbox_messages WHERE {OutboxModel.PendingFilter}").SingleAsync();
        var parked = await db.Database.SqlQueryRaw<long>(
            $"SELECT count(*) AS \"Value\" FROM module.outbox_messages WHERE {OutboxModel.ParkedFilter}").SingleAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(pending).IsEqualTo(0L);
            await Assert.That(parked).IsEqualTo(1L);
            await Assert.That(capture.Of(MessagingMetrics.OutboxParkedMetric).Single().Value).IsEqualTo(1d);
            await Assert.That(capture.Of(MessagingMetrics.OutboxOldestAgeMetric).Single().Value).IsEqualTo(599d);
        }
    }

    [Test]
    public async Task Both_gauges_are_read_from_the_pending_and_parked_indexes_in_one_lookup()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, 3, 10, "NULL");
        var commands = new CommandLog();
        var logged = new DbContextOptionsBuilder<InboxTestContext>(options).AddInterceptors(commands).Options;
        var (services, relay, capture) = Relay(logged, TimeProvider.System, new Accepting());
        await using var _ = services;
        using var __ = capture;

        await relay.PublishPendingAsync(CancellationToken.None);
        var sample = commands.Commands.Single(c => c.Text.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));

        // Act
        var plan = await AnalyzeWithoutSequentialScansAsync(options, sample);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(plan).Contains(OutboxModel.PendingIndex);
            await Assert.That(plan).Contains(OutboxModel.ParkedIndex);
        }
    }

    [Test]
    public async Task Raising_the_attempt_limit_does_not_revive_a_parked_message()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, 1, 10, "NULL");
        var (services, relay, capture) = Relay(options, TimeProvider.System, new Accepting(), new OutboxRelayOptions { MaxAttempts = 20 });
        await using var _ = services;
        using var __ = capture;

        // Act
        var published = await relay.PublishPendingAsync(CancellationToken.None);

        // Assert
        await Assert.That(published).IsEqualTo(0);
    }

    [Test]
    public async Task A_message_past_a_lowered_attempt_limit_gets_one_more_attempt_and_is_parked_when_it_fails()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, 1, 5, $"'{Occurred:O}'");
        var publisher = new Rejecting();
        var (services, relay, capture) = Relay(
            options,
            new FakeTimeProvider(Occurred.AddMinutes(10)),
            publisher,
            new OutboxRelayOptions { MaxAttempts = 3 });
        await using var _ = services;
        using var __ = capture;

        await relay.PublishPendingAsync(CancellationToken.None);
        capture.Observe();

        await using var db = new InboxTestContext(options);

        // Act
        var row = await db.Set<OutboxMessage>().SingleAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(publisher.Attempts).IsEqualTo(1);
            await Assert.That(row.Attempts).IsEqualTo(6);
            await Assert.That(row.NextAttemptAt).IsNull();
            await Assert.That(capture.Of(MessagingMetrics.OutboxParkedMetric).Single().Value).IsEqualTo(1d);
        }
    }

    /// <summary>
    /// Runs <paramref name="command"/> under <c>EXPLAIN ANALYZE</c> with sequential scans priced out (on a few test rows
    /// a scan always wins) and rolls it back; the plan shows which index served it and what it filtered out after.
    /// </summary>
    private static async Task<string> AnalyzeWithoutSequentialScansAsync(DbContextOptions<InboxTestContext> options, LoggedCommand command)
    {
        await using var db = new InboxTestContext(options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var off = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction))
        {
            await off.ExecuteNonQueryAsync();
        }

        await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, COSTS OFF, TIMING OFF, SUMMARY OFF) " + command.Text, connection, transaction);
        foreach (var (name, value) in command.Parameters)
        {
            explain.Parameters.Add(new NpgsqlParameter(name, value));
        }

        var lines = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }
        }

        await transaction.RollbackAsync();
        return string.Join('\n', lines);
    }

    /// <summary>A SQL command the interceptor saw: its text and parameters.</summary>
    private sealed record LoggedCommand(string Text, IReadOnlyList<(string Name, object? Value)> Parameters);

    /// <summary>Every command the relay's context sends, with its parameters, and how many of them read.</summary>
    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<LoggedCommand> _commands = new();

        public IReadOnlyList<LoggedCommand> Commands => [.. _commands];

        public int Selects => _commands.Count(c => c.Text.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(new LoggedCommand(
                command.CommandText,
                [.. command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value))]));
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>A publisher that accepts every message.</summary>
    private sealed class Accepting : IMessagePublisher
    {
        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A publisher that rejects every message for good.</summary>
    private sealed class Rejecting : IMessagePublisher
    {
        public int Attempts { get; private set; }

        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new MessageRejectedException("The stream refused it.");
        }
    }
}
