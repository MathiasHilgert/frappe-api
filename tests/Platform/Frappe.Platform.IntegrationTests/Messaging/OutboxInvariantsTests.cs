using Frappe.Messaging;
using Frappe.Messaging.Relay;
using Frappe.Messaging.Storage.Outbox;
using Frappe.Platform.Testing.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>The outbox's CHECK constraints against real Postgres: a row that breaks one is refused, and no relay path writes such a row.</summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class OutboxInvariantsTests(InboxDatabase database)
{
    private const string Module = "test";

    /// <summary>Where operators read the requeue statement: the XML doc of the option that parks messages.</summary>
    private const string OptionsSource = "src/Platform/Frappe.Messaging/Relay/OutboxRelayOptions.cs";

    private static readonly DateTimeOffset Occurred = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static async Task ExecuteAsync(DbContextOptions<InboxTestContext> options, string sql)
    {
        await using var db = new InboxTestContext(options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts one row with <c>attempts</c> (a SQL literal) and any further columns and values (SQL, each starting with a comma).</summary>
    private static Task InsertAsync(DbContextOptions<InboxTestContext> options, string attempts = "0", string columns = "", string values = "") =>
        ExecuteAsync(options, $"""
            INSERT INTO module.outbox_messages (id, type, source, data_content_type, data, occurred_at, attempts{columns})
            VALUES (
              gen_random_uuid(), 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '{"{}"}', '{Occurred:O}',
              {attempts}{values})
            """);

    private static async Task<string?> RefusedByAsync(Task statement)
    {
        var refused = await Assert.ThrowsAsync<PostgresException>(async () => await statement);
        await Assert.That(refused!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
        return refused.ConstraintName;
    }

    private static (ServiceProvider Services, OutboxRelay<InboxTestContext> Relay) Relay(
        DbContextOptions<InboxTestContext> options, TimeProvider time, IMessagePublisher publisher, OutboxRelayOptions? relayOptions = null)
    {
        var services = new ServiceCollection().AddMetrics().AddScoped(_ => new InboxTestContext(options)).BuildServiceProvider();
        var factory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        var relay = new OutboxRelay<InboxTestContext>(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            time,
            relayOptions ?? new OutboxRelayOptions(),
            Module,
            new MessagingMetrics(factory, time),
            NullLogger<OutboxRelay<InboxTestContext>>.Instance);
        return (services, relay);
    }

    private static async Task<OutboxMessage> OnlyRowAsync(DbContextOptions<InboxTestContext> options)
    {
        await using var db = new InboxTestContext(options);
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync();
    }

    [Test]
    public async Task The_model_declares_exactly_the_four_invariants_the_migrations_froze()
    {
        // Act
        // The AddOutboxInvariants migrations hold these as literals. Changing one here needs a new migration, not an edit of those.
        (string Name, string Sql)[] frozen =
        [
            ("ck_outbox_messages_attempts_nonnegative", "attempts >= 0"),
            ("ck_outbox_messages_next_attempt_after_failure", "next_attempt_at IS NULL OR attempts > 0"),
            ("ck_outbox_messages_last_error_after_failure", "last_error IS NULL OR attempts > 0"),
            ("ck_outbox_messages_published_settled", "published_at IS NULL OR (leased_until IS NULL AND last_error IS NULL)"),
        ];

        // Assert
        await Assert.That(OutboxModel.Invariants.ToArray()).IsEquivalentTo(frozen);
    }

    [Test]
    public async Task A_negative_attempt_count_is_refused_by_I1()
    {
        // Act
        var options = await database.CreateAsync();

        var constraint = await RefusedByAsync(InsertAsync(options, "-1"));

        // Assert
        await Assert.That(constraint).IsEqualTo(OutboxModel.AttemptsNonNegative);
    }

    [Test]
    public async Task A_next_attempt_time_on_a_row_that_never_failed_is_refused_by_I2()
    {
        // Act
        var options = await database.CreateAsync();

        var constraint = await RefusedByAsync(InsertAsync(options, "0", ", next_attempt_at", $", '{Occurred:O}'"));

        // Assert
        await Assert.That(constraint).IsEqualTo(OutboxModel.NextAttemptAfterFailure);
    }

    [Test]
    public async Task A_last_error_on_a_row_that_never_failed_is_refused_by_I3()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options);

        // Act
        var constraint = await RefusedByAsync(ExecuteAsync(options, "UPDATE module.outbox_messages SET last_error = 'boom'"));

        // Assert
        await Assert.That(constraint).IsEqualTo(OutboxModel.LastErrorAfterFailure);
    }

    [Test]
    public async Task A_published_row_that_still_holds_a_lease_is_refused_by_I4()
    {
        // Act
        var options = await database.CreateAsync();

        var constraint = await RefusedByAsync(InsertAsync(options, "0", ", published_at, leased_until", $", '{Occurred:O}', '{Occurred:O}'"));

        // Assert
        await Assert.That(constraint).IsEqualTo(OutboxModel.PublishedSettled);
    }

    [Test]
    public async Task A_published_row_that_still_holds_an_error_is_refused_by_I4()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, "1", ", last_error", ", 'boom'");

        // Act
        var constraint = await RefusedByAsync(ExecuteAsync(options, $"UPDATE module.outbox_messages SET published_at = '{Occurred:O}'"));

        // Assert
        await Assert.That(constraint).IsEqualTo(OutboxModel.PublishedSettled);
    }

    [Test]
    public async Task Publishing_a_message_that_failed_before_clears_what_I4_forbids_and_keeps_its_next_attempt_time()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options, "2", ", next_attempt_at, last_error", $", '{Occurred:O}', 'boom'");
        var (services, relay) = Relay(options, new FakeTimeProvider(Occurred.AddMinutes(10)), new Accepting());
        await using var _ = services;

        // Act
        var published = await relay.PublishPendingAsync(CancellationToken.None);

        var row = await OnlyRowAsync(options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(published).IsEqualTo(1);
            await Assert.That(row.PublishedAt).IsNotNull();
            await Assert.That(row.LeasedUntil).IsNull();
            await Assert.That(row.LastError).IsNull();
            await Assert.That(row.NextAttemptAt).IsEqualTo(NodaTime.Instant.FromDateTimeOffset(Occurred));
        }
    }

    [Test]
    public async Task A_rejected_message_is_retried_with_a_next_attempt_time_and_an_error()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options);
        var (services, relay) = Relay(options, new FakeTimeProvider(Occurred.AddMinutes(10)), new Rejecting());
        await using var _ = services;

        await relay.PublishPendingAsync(CancellationToken.None);

        // Act
        var row = await OnlyRowAsync(options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(row.Attempts).IsEqualTo(1);
            await Assert.That(row.NextAttemptAt).IsNotNull();
            await Assert.That(row.LastError).IsNotNull();
        }
    }

    [Test]
    public async Task A_message_rejected_for_the_last_time_is_parked_with_its_error()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options);
        var (services, relay) = Relay(
            options,
            new FakeTimeProvider(Occurred.AddMinutes(10)),
            new Rejecting(),
            new OutboxRelayOptions { MaxAttempts = 1 });
        await using var _ = services;

        await relay.PublishPendingAsync(CancellationToken.None);

        // Act
        var row = await OnlyRowAsync(options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(row.Attempts).IsEqualTo(1);
            await Assert.That(row.NextAttemptAt).IsNull();
            await Assert.That(row.LastError).IsNotNull();
        }
    }

    [Test]
    public async Task A_message_the_broker_could_not_take_is_released_without_an_attempt()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options);
        var (services, relay) = Relay(options, new FakeTimeProvider(Occurred.AddMinutes(10)), new Unavailable());
        await using var _ = services;

        // Act
        var published = await relay.PublishPendingAsync(CancellationToken.None);

        var row = await OnlyRowAsync(options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(published).IsEqualTo(0);
            await Assert.That(row.Attempts).IsEqualTo(0);
            await Assert.That(row.LeasedUntil).IsNull();
        }
    }

    [Test]
    public async Task The_requeue_of_docs_operations_on_a_row_the_relay_parked_is_accepted_and_goes_out()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options);
        var time = new FakeTimeProvider(Occurred.AddMinutes(10));
        var (services, relay) = Relay(options, time, new Rejecting(), new OutboxRelayOptions { MaxAttempts = 1 });
        await using var _ = services;
        await relay.PublishPendingAsync(CancellationToken.None);
        var parked = await OnlyRowAsync(options);
        await ExecuteAsync(options, RequeueStatement(parked.Id));
        var (accepting, requeueRelay) = Relay(options, time, new Accepting());
        await using var __ = accepting;

        // Act
        var requeued = await requeueRelay.PublishPendingAsync(CancellationToken.None);

        // Assert
        var row = await OnlyRowAsync(options);
        using (Assert.Multiple())
        {
            await Assert.That(parked.LastError).IsNotNull();
            await Assert.That(requeued).IsEqualTo(1);
            await Assert.That(row.PublishedAt).IsNotNull();
        }
    }

    [Test]
    public async Task The_requeue_without_clearing_the_last_error_is_refused_by_I3()
    {
        // Arrange
        var options = await database.CreateAsync();
        await InsertAsync(options);
        var (services, relay) = Relay(
            options,
            new FakeTimeProvider(Occurred.AddMinutes(10)),
            new Rejecting(),
            new OutboxRelayOptions { MaxAttempts = 1 });
        await using var _ = services;
        await relay.PublishPendingAsync(CancellationToken.None);

        // Act
        var parked = await OnlyRowAsync(options);

        var constraint = await RefusedByAsync(ExecuteAsync(options,
            $"UPDATE module.outbox_messages SET attempts = 0, next_attempt_at = NULL WHERE id = '{parked.Id}'"));

        // Assert
        await Assert.That(constraint).IsEqualTo(OutboxModel.LastErrorAfterFailure);
    }

    /// <summary>
    /// The requeue statement exactly as <see cref="OutboxRelayOptions.MaxAttempts"/> documents it for operators, with the
    /// test schema and the row's id filled in.
    /// </summary>
    private static string RequeueStatement(Guid id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, OptionsSource)))
        {
            directory = directory.Parent;
        }

        var statement = File.ReadAllLines(Path.Combine(directory!.FullName, OptionsSource))
            .Select(line => line.Trim().TrimStart('/').Trim())
            .Single(line => line.StartsWith("UPDATE orders.outbox_messages SET attempts = 0", StringComparison.Ordinal));
        return statement.Replace("orders.outbox_messages", "module.outbox_messages", StringComparison.Ordinal)
            .Replace("{id}", id.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A publisher that accepts every message.</summary>
    private sealed class Accepting : IMessagePublisher
    {
        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A publisher that rejects every message for good.</summary>
    private sealed class Rejecting : IMessagePublisher
    {
        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) =>
            throw new MessageRejectedException("The stream refused it.");
    }

    /// <summary>A publisher that is unavailable, so the relay retries later.</summary>
    private sealed class Unavailable : IMessagePublisher
    {
        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The broker is down.");
    }
}
