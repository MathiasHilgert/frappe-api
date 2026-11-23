using Frappe.Health;
using Frappe.Persistence;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using Npgsql;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>
/// <c>AddModuleDatabase</c> registers everything a module's database needs in one call: its options, the context under
/// the connection limits, the unit of work, the critical readiness check, the clock and the metrics.
/// </summary>
public sealed class ModuleDatabaseRegistrationTests
{
    private const string Plain = "Host=db;Port=5433;Database=frappe;Username=frappe_orders;Password=secret";

    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=frappe;Username=frappe_orders;Password=secret;Timeout=1;Pooling=false";

    [Test]
    public async Task The_context_connects_with_the_connection_string_of_its_module_under_the_connection_limits()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Plain), ("Database:MaxPoolSize", "9"));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        using var host = builder.Build();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var connection = new NpgsqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<OrdersContext>().Database.GetConnectionString());
        await host.StopAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.Host).IsEqualTo("db");
            await Assert.That(connection.Username).IsEqualTo("frappe_orders");
            await Assert.That(connection.MaxPoolSize).IsEqualTo(9);
            await Assert.That(connection.CommandTimeout).IsEqualTo(35);
        }
    }

    [Test]
    public async Task The_context_keeps_its_migrations_history_in_the_schema_of_the_module()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Plain));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders_schema");
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var npgsql = scope.ServiceProvider.GetRequiredService<OrdersContext>().GetService<IDbContextOptions>()
            .Extensions.OfType<RelationalOptionsExtension>().Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(npgsql.MigrationsHistoryTableName).IsEqualTo(ModuleDatabase.MigrationsHistoryTable);
            await Assert.That(npgsql.MigrationsHistoryTableSchema).IsEqualTo("orders_schema");
        }
    }

    [Test]
    public async Task The_callback_adds_to_the_options_with_the_providers_services()
    {
        // Arrange
        IClock? seen = null;
        var builder = Builder(("ConnectionStrings:Orders", Plain));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders", (provider, options) =>
        {
            seen = provider.GetRequiredService<IClock>();
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var tracking = scope.ServiceProvider.GetRequiredService<OrdersContext>().ChangeTracker.QueryTrackingBehavior;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(tracking).IsEqualTo(QueryTrackingBehavior.NoTracking);
            await Assert.That(seen).IsNotNull();
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    public async Task A_missing_connection_string_stops_the_host_naming_the_key_of_the_module(string? connectionString)
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", connectionString));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        using var host = builder.Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Assert
        await Assert.That(failure!.Message).Contains("Configuration 'ConnectionStrings:Orders' is required.");
    }

    [Test]
    public async Task Each_module_validates_its_own_connection_string()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Plain));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        builder.Services.AddModuleDatabase<InvoicesContext>("invoices", "invoices");
        using var host = builder.Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.Message).Contains("Configuration 'ConnectionStrings:Invoices' is required.");
            await Assert.That(failure.Message).DoesNotContain("ConnectionStrings:Orders");
        }
    }

    [Test]
    public async Task The_unit_of_work_of_the_module_is_registered_under_its_name()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Plain));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var unitOfWork = scope.ServiceProvider.GetKeyedService<IUnitOfWork>("orders");

        // Assert
        await Assert.That(unitOfWork).IsNotNull();
    }

    [Test]
    public async Task Readiness_fails_when_the_database_does_not_answer_because_the_check_is_critical()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Unreachable));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        using var host = builder.Build();

        // Act
        var report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains(HealthProbes.Ready));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(report.Entries["orders-database"].Status).IsEqualTo(HealthStatus.Unhealthy);
        }
    }

    [Test]
    public async Task Every_module_adds_its_own_readiness_check()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Unreachable), ("ConnectionStrings:Invoices", Unreachable));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        builder.Services.AddModuleDatabase<InvoicesContext>("invoices", "invoices");
        using var host = builder.Build();

        // Act
        var report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains(HealthProbes.Ready));

        // Assert
        await Assert.That(report.Entries.Keys).IsEquivalentTo(["orders-database", "invoices-database"]);
    }

    [Test]
    public async Task The_clock_follows_the_time_provider_the_host_registered()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 5, 17, 8, 30, 0, TimeSpan.Zero));
        var builder = Builder(("ConnectionStrings:Orders", Plain));
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        using var host = builder.Build();

        // Act
        var now = host.Services.GetRequiredService<IClock>().GetCurrentInstant();

        // Assert
        await Assert.That(now).IsEqualTo(Instant.FromUtc(2030, 5, 17, 8, 30));
    }

    [Test]
    public async Task The_meters_of_the_module_can_be_created()
    {
        // Arrange
        var builder = Builder(("ConnectionStrings:Orders", Plain));
        builder.Services.AddModuleDatabase<OrdersContext>("orders", "orders");
        using var host = builder.Build();

        // Act
        var factory = host.Services.GetService<System.Diagnostics.Metrics.IMeterFactory>();

        // Assert
        await Assert.That(factory).IsNotNull();
    }

    [Test]
    public async Task Registering_the_same_context_twice_is_a_mistake()
    {
        // Arrange
        var services = new ServiceCollection().AddModuleDatabase<OrdersContext>("orders", "orders");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => services.AddModuleDatabase<OrdersContext>("orders", "orders"));

        // Assert
        await Assert.That(failure.Message).Contains(nameof(OrdersContext));
    }

    [Test]
    [Arguments("", "orders")]
    [Arguments("orders", " ")]
    public async Task A_module_and_a_schema_are_required(string module, string schema)
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddModuleDatabase<OrdersContext>(module, schema));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo(module.Length == 0 ? "module" : "schema");
    }

    private static HostApplicationBuilder Builder(params (string Key, string? Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, s.Value)));
        return builder;
    }

    /// <summary>A module database registered through the platform.</summary>
    private sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options);

    /// <summary>A second module database, to check modules do not share options.</summary>
    private sealed class InvoicesContext(DbContextOptions<InvoicesContext> options) : DbContext(options);
}
