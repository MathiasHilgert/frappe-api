using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Infrastructure.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Retention;
using Frappe.Testing;
using Frappe.Testing.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// The daily retention as the Worker's role runs it (S16, S19): sessions that expired or were revoked are ended with their fact, ended ones go
/// after 30 days, pending owners after 7 with all their data and a fact each (never one just verified), codes 7 days after expiry; in batches,
/// at each boundary, counted once committed. Rows are seeded and read as the owner, so a missing grant fails the test.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class RetentionTests(PostgresServer server)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string?> SmallBatches = new() { ["Identity:Retention:BatchSize"] = "2" };

    private static string At(string offset) => $"(timestamptz '2026-11-24 12:00:00+00' + interval '{offset}')";

    private static RetentionWorld Worker(string owner, Dictionary<string, string?>? settings = null, RecordingMessagePurge? messages = null) =>
        new(owner, settings ?? SmallBatches, messages);

    private static async Task<Guid> AddPrincipalAsync(string owner, string status = "active")
    {
        var id = Guid.CreateVersion7();
        await ScratchDatabase.ExecuteAsync(owner, $"""
            INSERT INTO identity.principals (id, kind, status, credential_stamp, created_at)
            VALUES ('{id}', 'owner', '{status}', gen_random_uuid(), {At("-100 days")})
            """);
        return id;
    }

    /// <summary>An owner with an address and a password; <paramref name="registered"/> is an interval from the fake now.</summary>
    private static async Task<Guid> AddOwnerAsync(string owner, string registered, bool verified = false)
    {
        var id = await AddPrincipalAsync(owner, verified ? "active" : "pending");
        await ScratchDatabase.ExecuteAsync(owner, $"""
            INSERT INTO identity.owners (principal_id, given_name, locale, registered_at, verified_at)
            VALUES ('{id}', 'Ana', 'es-AR', {At(registered)}, {(verified ? $"{At(registered)} + interval '1 minute'" : "NULL")});
            INSERT INTO identity.email_identifiers (id, principal_id, address, normalized_address)
            VALUES (gen_random_uuid(), '{id}', 'Ana-{id}@example.com', 'ana-{id}@example.com');
            INSERT INTO identity.password_credentials (principal_id, hash, changed_at) VALUES ('{id}', 'hash', {At("-100 days")});
            INSERT INTO identity.one_time_codes (id, principal_id, purpose, target, code_hmac, issued_at, expires_at)
            VALUES (gen_random_uuid(), '{id}', 'email_verification', 'ana@example.com', decode(repeat('ab', 32), 'hex'),
                {At("-100 days")}, {At("100 days")})
            """);
        return id;
    }

    /// <summary>A session of <paramref name="principal"/> whose instants are intervals from the fake now (<c>null</c> is not set).</summary>
    private static async Task<Guid> AddSessionAsync(
        string owner, Guid principal, string idle = "1 day", string absolute = "10 days", string? ended = null, string? revoked = null)
    {
        var id = Guid.CreateVersion7();
        await ScratchDatabase.ExecuteAsync(owner, $"""
            INSERT INTO identity.sessions (id, principal_id, token_hash, audience, methods, credential_stamp, opened_at, last_seen_at,
                idle_expires_at, absolute_expires_at, ended_at, ip_prefix, revoked_at, revocation_reason)
            VALUES ('{id}', '{principal}', sha256(convert_to('{id}', 'UTF8')), 'console', ARRAY['pwd'], gen_random_uuid(),
                {At("-100 days")}, {At("-100 days")}, {At(idle)}, {At(absolute)}, {(ended is null ? "NULL" : At(ended))}, '203.0.113.0/24',
                {(revoked is null ? "NULL" : At(revoked))}, {(revoked is null ? "NULL" : "'logout'")})
            """);
        return id;
    }

    private static async Task AddCodeAsync(string owner, Guid principal, string expires) => await ScratchDatabase.ExecuteAsync(owner, $"""
        INSERT INTO identity.one_time_codes (id, principal_id, purpose, target, code_hmac, issued_at, expires_at, invalidated_at)
        VALUES (gen_random_uuid(), '{principal}', 'password_reset', 'ana@example.com', decode(repeat('cd', 32), 'hex'),
            {At("-200 days")}, {At(expires)}, {At("-199 days")})
        """);

    /// <summary>An undo link of <paramref name="principal"/>; <paramref name="expires"/> and <paramref name="redeemed"/> are intervals.</summary>
    private static async Task AddUndoAsync(string owner, Guid principal, string expires, string? redeemed = null) =>
        await ScratchDatabase.ExecuteAsync(owner, $"""
            INSERT INTO identity.email_change_undos (id, principal_id, old_address, new_address, token_hmac, issued_at, expires_at, redeemed_at)
            VALUES (gen_random_uuid(), '{principal}', 'ana@example.com', 'luz-' || gen_random_uuid() || '@example.com',
                decode(repeat('ef', 32), 'hex'), {At("-300 days")}, {At(expires)}, {(redeemed is null ? "NULL" : At(redeemed))})
            """);

    /// <summary>A condition that holds once some statement is waiting for a lock.</summary>
    private static Func<Task<bool>> WaitingAsync(string owner) =>
        async () => (long)(await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM pg_locks WHERE NOT granted"))! > 0;

    private static async Task<long> CountAsync(string owner, string table, string? where = null) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM identity.{table} {where}"))!;

    private static async Task<bool> ExistsAsync(string owner, string table, string key, Guid id) =>
        await CountAsync(owner, table, $"WHERE {key} = '{id}'") == 1;

    private static async Task<DateTimeOffset?> EndedAtAsync(string owner, Guid session) =>
        await ScratchDatabase.ScalarAsync(owner, $"SELECT ended_at FROM identity.sessions WHERE id = '{session}'") is DateTime ended
            ? new DateTimeOffset(DateTime.SpecifyKind(ended, DateTimeKind.Utc))
            : null;

    private static async Task<List<string>> EventsAsync(string owner, string type)
    {
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT data::text FROM identity.outbox_messages WHERE type = $1 ORDER BY sequence", connection);
        command.Parameters.AddWithValue(type);
        await using var reader = await command.ExecuteReaderAsync();
        List<string> rows = [];
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Test]
    public async Task Sessions_that_expired_or_were_revoked_are_ended_at_the_boundary_with_a_fact_for_each_expiry()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            var principal = await AddPrincipalAsync(owner);
            var idledNow = await AddSessionAsync(owner, principal, idle: "0 seconds", absolute: "10 days");
            var idleNotYet = await AddSessionAsync(owner, principal, idle: "1 second", absolute: "10 days");
            var absoluteNow = await AddSessionAsync(owner, principal, idle: "0 seconds", absolute: "0 seconds");
            var absoluteNotYet = await AddSessionAsync(owner, principal, idle: "1 second", absolute: "1 second");
            var revoked = await AddSessionAsync(owner, principal, idle: "1 day", absolute: "2 days", revoked: "-1 hour");
            var endedBefore = await AddSessionAsync(owner, principal, idle: "-3 days", absolute: "-2 days", ended: "-2 days");

            // Act
            var ended = await worker.Sweeper.SweepAsync(CancellationToken.None);

            // Assert
            var events = await EventsAsync(owner, "frappe.identity.session_ended.v1");
            using (Assert.Multiple())
            {
                await Assert.That(ended).IsEqualTo(3);
                await Assert.That(await EndedAtAsync(owner, idledNow)).IsEqualTo(Start);
                await Assert.That(await EndedAtAsync(owner, absoluteNow)).IsEqualTo(Start);
                await Assert.That(await EndedAtAsync(owner, idleNotYet)).IsNull();
                await Assert.That(await EndedAtAsync(owner, absoluteNotYet)).IsNull();
                await Assert.That(await EndedAtAsync(owner, revoked)).IsEqualTo(Start);
                await Assert.That(await EndedAtAsync(owner, endedBefore)).IsEqualTo(Start.AddDays(-2));
                await Assert.That(events.Count).IsEqualTo(2);
                await Assert.That(events.Count(e => e.Contains(idledNow.ToString()) && e.Contains("\"reason\": \"idle\""))).IsEqualTo(1);
                await Assert.That(events.Count(e => e.Contains(absoluteNow.ToString()) && e.Contains("\"reason\": \"absolute\""))).IsEqualTo(1);
                await Assert.That(events.All(e => e.Contains(principal.ToString()))).IsTrue();
                await Assert.That(worker.Counts.Of(IdentityMetrics.SessionsEndedMetric, "idle")).IsEqualTo(1);
                await Assert.That(worker.Counts.Of(IdentityMetrics.SessionsEndedMetric, "absolute")).IsEqualTo(1);
                await Assert.That(worker.Counts.Of(IdentityMetrics.SessionsEndedMetric, "revoked")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task The_sweep_ends_a_backlog_in_batches_and_a_second_run_finds_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: five due sessions with a batch of two.
            await using var worker = Worker(owner);
            var principal = await AddPrincipalAsync(owner);
            for (var i = 0; i < 5; i++)
            {
                await AddSessionAsync(owner, principal, idle: "-1 hour", absolute: "1 day");
            }

            // Act
            var first = await worker.Sweeper.SweepAsync(CancellationToken.None);
            var second = await worker.Sweeper.SweepAsync(CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(first).IsEqualTo(5);
                await Assert.That(second).IsEqualTo(0);
                await Assert.That(await CountAsync(owner, "sessions", "WHERE ended_at IS NULL")).IsEqualTo(0);
                await Assert.That((await EventsAsync(owner, "frappe.identity.session_ended.v1")).Count).IsEqualTo(5);
            }
        });
    }

    [Test]
    public async Task A_session_touched_while_the_sweep_waits_for_it_is_skipped_without_a_fact()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: a due session whose row an open request has locked to extend its idle expiry.
            await using var worker = Worker(owner);
            var principal = await AddPrincipalAsync(owner);
            var touched = await AddSessionAsync(owner, principal, idle: "-1 minute", absolute: "1 day");
            var due = await AddSessionAsync(owner, principal, idle: "-1 minute", absolute: "1 day");
            await using var request = new NpgsqlConnection(owner);
            await request.OpenAsync();
            await using var transaction = await request.BeginTransactionAsync();
            await using (var touch = new NpgsqlCommand(
                $"UPDATE identity.sessions SET idle_expires_at = {At("12 hours")} WHERE id = '{touched}'", request, transaction))
            {
                await touch.ExecuteNonQueryAsync();
            }

            // Act
            var sweep = Task.Run(() => worker.Sweeper.SweepAsync(CancellationToken.None));
            await WaitUntil.TrueAsync(WaitingAsync(owner), "The sweep never waited for the touched session.");
            await transaction.CommitAsync();
            var ended = await sweep;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(ended).IsEqualTo(1);
                await Assert.That(await EndedAtAsync(owner, touched)).IsNull();
                await Assert.That(await EndedAtAsync(owner, due)).IsEqualTo(Start);
                await Assert.That((await EventsAsync(owner, "frappe.identity.session_ended.v1")).Count).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task Sessions_ended_more_than_thirty_days_ago_are_purged_in_batches_and_the_rest_stay()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: three old (batch of two), one at exactly thirty days, one just inside, one never ended.
            await using var worker = Worker(owner);
            var principal = await AddPrincipalAsync(owner);
            var old = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                old.Add(await AddSessionAsync(owner, principal, idle: "-40 days", absolute: "-40 days", ended: "-30 days -1 second"));
            }

            var exactly = await AddSessionAsync(owner, principal, idle: "-40 days", absolute: "-40 days", ended: "-30 days");
            var inside = await AddSessionAsync(owner, principal, idle: "-40 days", absolute: "-40 days", ended: "-29 days");
            var live = await AddSessionAsync(owner, principal);

            // Act
            var (sessions, _, _) = await worker.Purger.PurgeAsync(CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(sessions).IsEqualTo(3);
                foreach (var id in old)
                {
                    await Assert.That(await ExistsAsync(owner, "sessions", "id", id)).IsFalse();
                }

                await Assert.That(await ExistsAsync(owner, "sessions", "id", exactly)).IsTrue();
                await Assert.That(await ExistsAsync(owner, "sessions", "id", inside)).IsTrue();
                await Assert.That(await ExistsAsync(owner, "sessions", "id", live)).IsTrue();
                await Assert.That(worker.Counts.Of(IdentityMetrics.PurgedMetric, "sessions")).IsEqualTo(3);
            }
        });
    }

    [Test]
    public async Task Codes_expired_for_more_than_seven_days_are_purged_in_batches_and_the_rest_stay()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: the principal's own live code (expires in 100 days) is none of them.
            await using var worker = Worker(owner);
            var principal = await AddPrincipalAsync(owner);
            for (var i = 0; i < 3; i++)
            {
                await AddCodeAsync(owner, principal, "-7 days -1 second");
            }

            await AddCodeAsync(owner, principal, "-7 days");
            await AddCodeAsync(owner, principal, "-6 days");
            await AddCodeAsync(owner, principal, "1 hour");

            // Act
            var (_, codes, _) = await worker.Purger.PurgeAsync(CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(codes).IsEqualTo(3);
                await Assert.That(await CountAsync(owner, "one_time_codes")).IsEqualTo(3);
                await Assert.That(await CountAsync(owner, "one_time_codes", $"WHERE expires_at < {At("-7 days")}")).IsEqualTo(0);
                await Assert.That(worker.Counts.Of(IdentityMetrics.PurgedMetric, "codes")).IsEqualTo(3);
            }
        });
    }

    [Test]
    public async Task Undo_links_expired_or_used_for_more_than_seven_days_are_purged_in_batches_and_the_rest_stay()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: two expired long ago and one used long ago (batch of two), one at exactly seven days, one just inside, one live,
            // one live and used just inside.
            await using var worker = Worker(owner);
            var principal = await AddPrincipalAsync(owner);
            await AddUndoAsync(owner, principal, "-7 days -1 second");
            await AddUndoAsync(owner, principal, "-8 days");
            await AddUndoAsync(owner, principal, "20 days", redeemed: "-7 days -1 second");
            await AddUndoAsync(owner, principal, "-7 days");
            await AddUndoAsync(owner, principal, "-6 days");
            await AddUndoAsync(owner, principal, "1 hour");
            await AddUndoAsync(owner, principal, "20 days", redeemed: "-6 days");

            // Act
            var (_, _, undos) = await worker.Purger.PurgeAsync(CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(undos).IsEqualTo(3);
                await Assert.That(await CountAsync(owner, "email_change_undos")).IsEqualTo(4);
                await Assert.That(worker.Counts.Of(IdentityMetrics.PurgedMetric, "email_change_undos")).IsEqualTo(3);
            }
        });
    }

    [Test]
    public async Task Pending_owners_older_than_seven_days_go_with_their_data_and_a_fact_each_and_verified_owners_stay()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: three pending owners past the window (batch of two), one at the boundary, one inside, an old verified one.
            await using var worker = Worker(owner);
            List<Guid> old = [];
            for (var i = 0; i < 3; i++)
            {
                var id = await AddOwnerAsync(owner, "-7 days -1 second");
                await AddSessionAsync(owner, id);
                old.Add(id);
            }

            var boundary = await AddOwnerAsync(owner, "-7 days");
            var inside = await AddOwnerAsync(owner, "-6 days");
            var verified = await AddOwnerAsync(owner, "-400 days", verified: true);

            // Act
            var owners = await worker.Owners.PurgeAsync(CancellationToken.None);

            // Assert
            var events = await EventsAsync(owner, "frappe.identity.owner_pending_purged.v1");
            using (Assert.Multiple())
            {
                await Assert.That(owners).IsEqualTo(3);
                foreach (var id in old)
                {
                    await Assert.That(await ExistsAsync(owner, "principals", "id", id)).IsFalse();
                    await Assert.That(await ExistsAsync(owner, "owners", "principal_id", id)).IsFalse();
                    await Assert.That(await ExistsAsync(owner, "email_identifiers", "principal_id", id)).IsFalse();
                    await Assert.That(await ExistsAsync(owner, "password_credentials", "principal_id", id)).IsFalse();
                    await Assert.That(await ExistsAsync(owner, "one_time_codes", "principal_id", id)).IsFalse();
                    await Assert.That(await ExistsAsync(owner, "sessions", "principal_id", id)).IsFalse();
                    await Assert.That(events.Count(e => e.Contains(id.ToString()))).IsEqualTo(1);
                }

                foreach (var id in new[] { boundary, inside, verified })
                {
                    await Assert.That(await ExistsAsync(owner, "owners", "principal_id", id)).IsTrue();
                    await Assert.That(await ExistsAsync(owner, "email_identifiers", "principal_id", id)).IsTrue();
                }

                await Assert.That(events.Count).IsEqualTo(3);
                await Assert.That(worker.Counts.Of(IdentityMetrics.PurgedMetric, "owners")).IsEqualTo(3);
            }
        });
    }

    [Test]
    public async Task An_owner_verified_while_the_purge_waits_for_them_survives_without_a_fact_and_the_others_go()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange: the verification (principal first, then the owner) is open and uncommitted when the purge selects its batch.
            await using var worker = Worker(owner);
            var racing = await AddOwnerAsync(owner, "-8 days");
            var stale = await AddOwnerAsync(owner, "-9 days");
            await using var verification = new NpgsqlConnection(owner);
            await verification.OpenAsync();
            await using var transaction = await verification.BeginTransactionAsync();
            await using (var verify = new NpgsqlCommand($"""
                UPDATE identity.principals SET status = 'active' WHERE id = '{racing}';
                UPDATE identity.owners SET verified_at = {At("0 seconds")} WHERE principal_id = '{racing}'
                """, verification, transaction))
            {
                await verify.ExecuteNonQueryAsync();
            }

            // Act
            var purge = Task.Run(() => worker.Owners.PurgeAsync(CancellationToken.None));
            await WaitUntil.TrueAsync(WaitingAsync(owner), "The purge never waited for the verified owner.");
            await transaction.CommitAsync();
            var owners = await purge;

            // Assert
            var events = await EventsAsync(owner, "frappe.identity.owner_pending_purged.v1");
            using (Assert.Multiple())
            {
                await Assert.That(owners).IsEqualTo(1);
                await Assert.That(await ExistsAsync(owner, "principals", "id", racing)).IsTrue();
                await Assert.That(await ExistsAsync(owner, "owners", "principal_id", racing)).IsTrue();
                await Assert.That(await ExistsAsync(owner, "email_identifiers", "principal_id", racing)).IsTrue();
                await Assert.That(await ExistsAsync(owner, "principals", "id", stale)).IsFalse();
                await Assert.That(events.Count).IsEqualTo(1);
                await Assert.That(events.Single()).Contains(stale.ToString());
            }
        });
    }

    [Test]
    public async Task The_job_sweeps_purges_and_then_purges_the_modules_messages()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var messages = new RecordingMessagePurge();
            await using var worker = Worker(owner, messages: messages);
            var pending = await AddOwnerAsync(owner, "-8 days");
            var principal = await AddPrincipalAsync(owner);
            var due = await AddSessionAsync(owner, principal, idle: "-1 hour", absolute: "1 day");
            var old = await AddSessionAsync(owner, principal, idle: "-40 days", absolute: "-40 days", ended: "-31 days");
            var job = ActivatorUtilities.CreateInstance<IdentityRetentionJob>(worker.Services);

            // Act
            await job.Execute(null!, CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await EndedAtAsync(owner, due)).IsEqualTo(Start);
                await Assert.That(await ExistsAsync(owner, "sessions", "id", old)).IsFalse();
                await Assert.That(await ExistsAsync(owner, "principals", "id", pending)).IsFalse();
                await Assert.That(messages.Runs).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_failing_message_purge_does_not_skip_the_rows_and_fails_the_job()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner, messages: new RecordingMessagePurge { Fails = true });
            var pending = await AddOwnerAsync(owner, "-8 days");
            var job = ActivatorUtilities.CreateInstance<IdentityRetentionJob>(worker.Services);

            // Act
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await job.Execute(null!, CancellationToken.None));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(thrown!.Message).Contains("messages");
                await Assert.That(await ExistsAsync(owner, "principals", "id", pending)).IsFalse();
            }
        });
    }

    /// <summary>The Worker's composition for Identity connected as the Worker's role, with the clock at <see cref="Start"/>.</summary>
    private sealed class RetentionWorld : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public RetentionWorld(string owner, Dictionary<string, string?> settings, RecordingMessagePurge? messages)
        {
            settings["ConnectionStrings:Identity"] = ScratchDatabase.LoginOf(owner, IdentityDatabase.WorkerRole, MigratedIdentity.WorkerPassword);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
            new IdentityModule().AddServices(services);
            new IdentityWorkerModule().AddServices(services);
            if (messages is not null)
            {
                services.AddSingleton<IMessagePurge>(messages);
            }

            _provider = services.BuildServiceProvider();
            Counts = new MetricCounts();
        }

        public MetricCounts Counts { get; }

        public IServiceProvider Services => _provider;

        public SessionSweeper Sweeper => _provider.GetRequiredService<SessionSweeper>();

        public StaleRowsPurger Purger => _provider.GetRequiredService<StaleRowsPurger>();

        public PendingOwnerPurger Owners => _provider.GetRequiredService<PendingOwnerPurger>();

        public async ValueTask DisposeAsync()
        {
            Counts.Dispose();
            await _provider.DisposeAsync();
        }
    }

    /// <summary>Counts what the module's counters record while the test runs, by their first tag.</summary>
    private sealed class MetricCounts : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, string Tag, long Value)> _measurements = [];

        public MetricCounts()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == IdentityMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                lock (_measurements)
                {
                    _measurements.Add((instrument.Name, tags.ToArray().Select(t => t.Value?.ToString()).FirstOrDefault() ?? string.Empty, value));
                }
            });
            _listener.Start();
        }

        public long Of(string name, string tag)
        {
            lock (_measurements)
            {
                return _measurements.Where(m => m.Name == name && m.Tag == tag).Sum(m => m.Value);
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>The Worker's message purge, counting its runs and failing on request.</summary>
    private sealed class RecordingMessagePurge : IMessagePurge
    {
        public int Runs { get; private set; }

        public bool Fails { get; init; }

        public Task PurgeAsync(CancellationToken cancellationToken)
        {
            Runs++;
            return Fails ? throw new InvalidOperationException("The messages purge failed.") : Task.CompletedTask;
        }
    }
}
