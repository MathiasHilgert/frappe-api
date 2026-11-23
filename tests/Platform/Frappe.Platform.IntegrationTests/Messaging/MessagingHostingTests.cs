using System.Collections.Concurrent;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Relay;
using Frappe.Platform.Testing.Messaging;
using Frappe.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// What a host runs for messaging: the relay and the topology service publish what the outbox holds, a consumer reads it
/// into the inbox, the NATS health check reports the broker, and none of them stops the host when a dependency is down.
/// </summary>
[ClassDataSource<InboxDatabase, NatsServer>(Shared = [SharedType.PerTestSession, SharedType.PerTestSession])]
public sealed class MessagingHostingTests(InboxDatabase database, NatsServer nats)
{
    private const string Module = "test";

    private const string ClosedPort = "nats://127.0.0.1:1";

    /// <summary>Keeps what the services under test log, so a test can wait for the line that proves a failure was handled.</summary>
    private sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(_lines);

        public void Dispose()
        {
        }

        /// <summary>Appends every formatted message to the shared queue.</summary>
        private sealed class CaptureLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>The handler a consumer test registers: it records what it received in the consumer's transaction.</summary>
    private sealed class RecordingHandler(InboxTestContext db) : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            db.Effects.Add(new HandledEffect { Id = Guid.NewGuid(), Step = integrationEvent.What });
            return Task.CompletedTask;
        }
    }

    private static IConfiguration FastConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Messaging:Relay:PollInterval"] = "00:00:00.100",
        ["Messaging:Relay:RetryBackoff:0"] = "00:00:00.100",
        ["Messaging:Consumers:Backoff:0"] = "00:00:00.100",
    }).Build();

    private static ServiceCollection Services(LogCapture logs, string url, string? @namespace)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddSingleton(FastConfiguration());
        services.AddJetStream(url, "frappe-tests", @namespace);
        return services;
    }

    private static async Task StartAsync(ServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    private static async Task StopAsync(ServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task InsertOutboxAsync(DbContextOptions<InboxTestContext> options)
    {
        await using var db = new InboxTestContext(options);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO module.outbox_messages (id, type, source, data_content_type, data, occurred_at, attempts)
            VALUES ({Guid.NewGuid()}, 'frappe.test.something_happened.v1', '/frappe/test', 'application/json', '"hello"', now(), 0)
            """);
    }

    private static async Task<long> ScalarAsync(DbContextOptions<InboxTestContext> options, string sql)
    {
        await using var db = new InboxTestContext(options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task The_relay_and_the_topology_service_publish_the_outbox_into_the_modules_stream_for_the_life_of_the_host()
    {
        // Arrange
        var options = await database.CreateAsync();
        var naming = NatsServer.FreshNaming();
        var logs = new LogCapture();
        var services = Services(logs, nats.Url, naming.Namespace);
        services.AddScoped(_ => new InboxTestContext(options));
        services.AddOutboxRelay<InboxTestContext>(Module);
        await using var provider = services.BuildServiceProvider();
        await InsertOutboxAsync(options);

        // Act
        await StartAsync(provider);
        await WaitUntil.TrueAsync(
            async () => await ScalarAsync(options, "SELECT count(*) FROM module.outbox_messages WHERE published_at IS NOT NULL") == 1,
            "The relay never published the outbox row.");
        await StopAsync(provider);

        // Assert
        var stream = await nats.JetStream.GetStreamAsync(naming.Stream(Module));
        var deadLetters = await nats.JetStream.GetStreamAsync(naming.DeadLetterStream);
        using (Assert.Multiple())
        {
            await Assert.That(stream.Info.State.Messages).IsEqualTo(1);
            await Assert.That(deadLetters.Info.Config.Name).IsEqualTo(naming.DeadLetterStream);
        }
    }

    [Test]
    public async Task A_relay_whose_database_is_down_logs_the_failure_and_keeps_running_until_it_is_stopped()
    {
        // Arrange
        var logs = new LogCapture();
        var naming = NatsServer.FreshNaming();
        var down = database.Unreachable();
        var services = Services(logs, nats.Url, naming.Namespace);
        services.AddScoped(_ => new InboxTestContext(down));
        services.AddOutboxRelay<InboxTestContext>(Module);
        await using var provider = services.BuildServiceProvider();

        // Act
        await StartAsync(provider);
        await WaitUntil.TrueAsync(
            () => logs.Lines.Count(line => line.Contains("Outbox relay for InboxTestContext failed", StringComparison.Ordinal)) >= 2,
            "The relay did not log its failures and retry.");
        await StopAsync(provider);

        // Assert
        await Assert.That(logs.Lines.Any(line => line.Contains("failure 2", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task The_topology_service_retries_while_the_broker_is_unreachable_and_never_stops_the_host()
    {
        // Arrange
        var logs = new LogCapture();
        var services = Services(logs, ClosedPort, null);
        services.AddScoped(_ => new InboxTestContext(new DbContextOptions<InboxTestContext>()));
        services.AddOutboxRelay<InboxTestContext>(Module);
        await using var provider = services.BuildServiceProvider();

        // Act
        await StartAsync(provider);
        await WaitUntil.TrueAsync(
            () => logs.Lines.Any(line => line.Contains("JetStream streams could not be ensured", StringComparison.Ordinal)),
            "The topology service did not report the unreachable broker.",
            TimeSpan.FromSeconds(60));
        await StopAsync(provider);

        // Assert
        await Assert.That(logs.Lines.Any(line => line.Contains("retrying in", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task The_nats_health_check_is_healthy_while_the_broker_answers()
    {
        // Arrange
        var services = Services(new LogCapture(), nats.Url, null);
        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await health.CheckHealthAsync(registration => registration.Name == "nats");

        // Assert
        await Assert.That(report.Entries["nats"].Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task The_nats_health_check_is_unhealthy_when_the_broker_does_not_answer()
    {
        // Arrange
        var services = Services(new LogCapture(), ClosedPort, null);
        await using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await health.CheckHealthAsync(registration => registration.Name == "nats");

        // Assert
        await Assert.That(report.Entries["nats"].Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task A_registered_consumer_reads_the_modules_stream_into_the_inbox_and_runs_its_handler()
    {
        // Arrange
        var options = await database.CreateAsync();
        var naming = NatsServer.FreshNaming();
        var services = Services(new LogCapture(), nats.Url, naming.Namespace);
        services.AddScoped(_ => new InboxTestContext(options));
        services.AddScoped<IIntegrationEventHandler<SomethingHappened>, RecordingHandler>();
        services.AddIntegrationEventConsumer<InboxTestContext, SomethingHappened>(Module, "test-reader");
        await using var provider = services.BuildServiceProvider();
        await StartAsync(provider);
        await WaitUntil.TrueAsync(
            async () =>
            {
                try
                {
                    await nats.JetStream.GetConsumerAsync(naming.Stream(Module), "test-reader");
                    return true;
                }
                catch (Exception exception) when (exception is NATS.Client.JetStream.NatsJSApiException)
                {
                    return false;
                }
            },
            "The consumer was never created.");
        var publisher = provider.GetRequiredService<IMessagePublisher>();

        // Act
        await publisher.PublishAsync(
            new OutgoingMessage(
                Guid.NewGuid(),
                "frappe.test.something_happened.v1",
                "/frappe/test",
                "application/json",
                System.Text.Encoding.UTF8.GetBytes("{\"what\":\"from-the-stream\"}"),
                Instant.FromUtc(2026, 11, 16, 12, 0),
                null,
                null),
            CancellationToken.None);
        await WaitUntil.TrueAsync(
            async () => await ScalarAsync(options, "SELECT count(*) FROM module.effects WHERE \"Step\" = 'from-the-stream'") == 1,
            "The consumer never handled the message.");
        await StopAsync(provider);

        // Assert
        await Assert.That(await ScalarAsync(options, "SELECT count(*) FROM module.inbox_messages")).IsEqualTo(1);
    }

    [Test]
    public async Task A_consumer_that_cannot_reach_the_broker_logs_it_and_keeps_trying_until_stopped()
    {
        // Arrange
        var logs = new LogCapture();
        var services = Services(logs, ClosedPort, null);
        services.AddScoped(_ => new InboxTestContext(new DbContextOptions<InboxTestContext>()));
        services.AddScoped<IIntegrationEventHandler<SomethingHappened>, RecordingHandler>();
        services.AddIntegrationEventConsumer<InboxTestContext, SomethingHappened>(Module, "test-reader");
        await using var provider = services.BuildServiceProvider();

        // Act
        await StartAsync(provider);
        await WaitUntil.TrueAsync(
            () => logs.Lines.Any(line => line.Contains("lost its subscription", StringComparison.Ordinal)),
            "The consumer did not report its failed subscription.",
            TimeSpan.FromSeconds(60));
        await StopAsync(provider);

        // Assert
        await Assert.That(logs.Lines.Any(line => line.Contains("test-reader", StringComparison.Ordinal))).IsTrue();
    }
}
