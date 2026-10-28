using Alba;
using Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;
using Frappe.Platform.IntegrationTests.Persistence;
using Frappe.Platform.Modularity;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Metrics;

namespace Frappe.Platform.IntegrationTests.Messaging;

// The platform with three fixture modules on a database of its own, so no other host in the run
// recovers or clears this host's envelopes, and on a NATS scope of its own. Development, so Wolverine runs
// Solo and creates the NATS topology.
public class MessagingFixture(AppFixture app) : IAsyncLifetime
{
    private const string FixtureSchemas = """
        CREATE SCHEMA kitchen;
        CREATE TABLE kitchen.tickets (
            id uuid PRIMARY KEY, business_id uuid NOT NULL, note text NOT NULL, in_transaction boolean NOT NULL);
        CREATE SCHEMA billing;
        CREATE TABLE billing.invoices (ticket_id uuid PRIMARY KEY, business_id uuid NOT NULL);
        CREATE SCHEMA audit;
        CREATE TABLE audit.entries (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, ticket_id uuid NOT NULL);
        GRANT USAGE ON SCHEMA kitchen, billing, audit TO frappe_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA kitchen, billing, audit TO frappe_app;
        """;

    private readonly FrappeDatabase _database = new();
    private NpgsqlDataSource? _owner;
    private IAlbaHost? _host;

    public IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialized.");

    internal LogCapture Logs { get; } = new();

    internal TransactionTenants Transactions { get; } = new();

    internal NatsScope Nats { get; } = NatsScope.On(app.Nats);

    // The assembly's NATS server, for fixtures whose tests take scopes of their own.
    internal FrappeNats SharedNats { get; } = app.Nats;

    // The exporter adds from its own thread while a test reads.
    internal LockedCollection<MetricSnapshot> Metrics { get; } = new();

    // The environment of the fixture's own host, or null for a fixture whose tests start their own hosts.
    protected virtual string? HostEnvironment => Environments.Development;

    public virtual async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _owner = NpgsqlDataSource.Create(_database.OwnerConnectionString);
        await using (NpgsqlCommand schemas = _owner.CreateCommand(FixtureSchemas))
        {
            await schemas.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        if (HostEnvironment is not { } environment)
        {
            return;
        }

        if (!string.Equals(environment, Environments.Development, StringComparison.Ordinal))
        {
            await ProvisionAsync(Nats);
        }

        _host = await StartHostAsync(Nats, environment);
    }

    // Deployed environments only verify the NATS topology; a Development host creates it, as the deploy step does.
    public async Task ProvisionAsync(NatsScope nats, IModule[]? modules = null) =>
        await (
            await StartHostAsync(nats, Environments.Development, "worker", modules)
        ).DisposeAsync();

    // A host on this fixture's database: Kitchen, Billing and Audit unless other modules are given.
    public Task<IAlbaHost> StartHostAsync(
        NatsScope nats,
        string environment,
        string role = "all",
        IModule[]? modules = null,
        Action<WebApplicationBuilder>? configure = null
    ) =>
        FrappeTestHost.StartAsync(
            _database.AppConnectionString,
            modules ?? [new KitchenModule(), new BillingModule(), new AuditModule()],
            builder =>
            {
                builder.Configuration["Platform:Role"] = role;
                builder.Logging.AddProvider(Logs);
                builder.Services.ConfigureDbContext<KitchenDbContext>(
                    Intercept,
                    ServiceLifetime.Singleton
                );
                builder.Services.ConfigureDbContext<BillingDbContext>(
                    Intercept,
                    ServiceLifetime.Singleton
                );
                builder.Services.ConfigureDbContext<AuditDbContext>(
                    Intercept,
                    ServiceLifetime.Singleton
                );
                builder
                    .Services.AddOpenTelemetry()
                    .WithMetrics(metrics => metrics.AddInMemoryExporter(Metrics));
                configure?.Invoke(builder);
            },
            environment,
            nats
        );

    // Owner reads bypass row-level security, so a test sees every row its handlers wrote.
    public async Task<long> CountAsync(string sql, Guid id)
    {
        await using NpgsqlCommand command = _owner!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return (long)(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0L
        );
    }

    // Waits for every transaction still writing to the table to commit, then counts: a SHARE lock queues behind
    // their ROW EXCLUSIVE locks. For a test whose handler ran outside any tracked session it can wait on.
    public async Task<long> CountCommittedAsync(string table, string sql, Guid id)
    {
        await using NpgsqlConnection connection = await _owner!.OpenConnectionAsync(
            TestContext.Current.CancellationToken
        );
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );
        await using (
            NpgsqlCommand lockTable = new(
                $"LOCK TABLE {table} IN SHARE MODE",
                connection,
                transaction
            )
        )
        {
            await lockTable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using NpgsqlCommand count = new(sql, connection, transaction);
        count.Parameters.AddWithValue(id);
        return (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0L);
    }

    public async Task ExecuteAsync(string sql, Guid id)
    {
        await using NpgsqlCommand command = _owner!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T?> ScalarAsync<T>(string sql, Guid id)
    {
        await using NpgsqlCommand command = _owner!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull or null ? default : (T)value;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        if (_owner is not null)
        {
            await _owner.DisposeAsync();
        }

        await _database.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // Singleton, as the contexts' own options are.
    private void Intercept(IServiceProvider services, DbContextOptionsBuilder options) =>
        options.AddInterceptors(Transactions);
}
