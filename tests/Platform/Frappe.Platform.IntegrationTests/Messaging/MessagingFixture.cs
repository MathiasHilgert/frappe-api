using Alba;
using Frappe.Platform.IntegrationTests.Messaging.Audit.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Billing.Infrastructure;
using Frappe.Platform.IntegrationTests.Messaging.Kitchen.Infrastructure;
using Frappe.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Metrics;

namespace Frappe.Platform.IntegrationTests.Messaging;

// The platform with three fixture modules on a database of its own, so no other host in the run
// recovers or clears this host's envelopes. Development, so Wolverine runs Solo.
public class MessagingFixture : IAsyncLifetime
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

    // The exporter adds from its own thread while a test reads.
    internal LockedCollection<MetricSnapshot> Metrics { get; } = new();

    protected virtual string HostEnvironment => Environments.Development;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _owner = NpgsqlDataSource.Create(_database.OwnerConnectionString);
        await using (NpgsqlCommand schemas = _owner.CreateCommand(FixtureSchemas))
        {
            await schemas.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        _host = await FrappeTestHost.StartAsync(
            _database.AppConnectionString,
            [new KitchenModule(), new BillingModule(), new AuditModule()],
            builder =>
            {
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
            },
            HostEnvironment
        );
    }

    // Owner reads bypass row-level security, so a test sees every row its handlers wrote.
    public async Task<long> CountAsync(string sql, Guid id)
    {
        await using NpgsqlCommand command = _owner!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return (long)(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0L
        );
    }

    public async Task<T?> ScalarAsync<T>(string sql, Guid id)
    {
        await using NpgsqlCommand command = _owner!.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull or null ? default : (T)value;
    }

    public async ValueTask DisposeAsync()
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
