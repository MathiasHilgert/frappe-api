using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>
/// Every module database connects under a bounded pool and a command timeout: the defaults, or <c>Database:*</c>, or
/// what the operator wrote into the connection string itself, which always wins.
/// </summary>
public sealed class ConnectionLimitsTests
{
    private const string Plain = "Host=db;Port=5433;Database=frappe;Username=frappe_orders;Password=secret";

    [Test]
    public async Task A_module_connection_gets_the_default_pool_size_and_command_timeout()
    {
        // Act
        var connection = Parse(ModuleDatabase.WithConnectionLimits(Plain, new DatabaseOptions()));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(6);
            await Assert.That(connection.CommandTimeout).IsEqualTo(35);
            await Assert.That(connection.Host).IsEqualTo("db");
            await Assert.That(connection.Port).IsEqualTo(5433);
            await Assert.That(connection.Database).IsEqualTo("frappe");
            await Assert.That(connection.Username).IsEqualTo("frappe_orders");
            await Assert.That(connection.Password).IsEqualTo("secret");
        }
    }

    [Test]
    public async Task Configured_limits_replace_the_defaults()
    {
        // Arrange
        var limits = new DatabaseOptions { MaxPoolSize = 12, CommandTimeout = TimeSpan.FromSeconds(20) };

        // Act
        var connection = Parse(ModuleDatabase.WithConnectionLimits(Plain, limits));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(12);
            await Assert.That(connection.CommandTimeout).IsEqualTo(20);
        }
    }

    [Test]
    [Arguments("Maximum Pool Size=40;Command Timeout=90")]
    [Arguments("MaxPoolSize=40;CommandTimeout=90")]
    public async Task Limits_written_in_the_connection_string_win(string written)
    {
        // Arrange
        var limits = new DatabaseOptions { MaxPoolSize = 12, CommandTimeout = TimeSpan.FromSeconds(20) };

        // Act
        var connection = Parse(ModuleDatabase.WithConnectionLimits($"{Plain};{written}", limits));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(40);
            await Assert.That(connection.CommandTimeout).IsEqualTo(90);
        }
    }

    [Test]
    public async Task A_minimum_pool_size_above_the_default_raises_the_maximum_to_it()
    {
        // Act
        var connection = Parse(ModuleDatabase.WithConnectionLimits($"{Plain};Minimum Pool Size=10", new DatabaseOptions()));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MinPoolSize).IsEqualTo(10);
            await Assert.That(connection.MaxPoolSize).IsEqualTo(10);
        }
    }

    [Test]
    public async Task A_registered_module_context_connects_with_the_configured_limits()
    {
        // Arrange
        var builder = Builder(("Database:MaxPoolSize", "9"));
        builder.Services.AddModuleDbContext<LimitsContext>(_ => Plain, (_, options, connection) => options.UseNpgsql(connection));
        using var host = builder.Build();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var connection = Parse(scope.ServiceProvider.GetRequiredService<LimitsContext>().Database.GetConnectionString()!);
        await host.StopAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(9);
            await Assert.That(connection.CommandTimeout).IsEqualTo(35);
        }
    }

    [Test]
    public async Task A_readiness_check_connects_through_a_pool_of_one_with_the_configured_command_timeout()
    {
        // Arrange
        var builder = Builder(("Database:MaxPoolSize", "9"), ("Database:CommandTimeout", "00:00:20"));
        builder.Services.AddDatabaseOptions();
        using var host = builder.Build();

        // Act
        var connection = Parse(host.Services.ReadinessConnectionString(Plain));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(1);
            await Assert.That(connection.CommandTimeout).IsEqualTo(20);
            await Assert.That(connection.Username).IsEqualTo("frappe_orders");
        }
    }

    [Test]
    public async Task A_readiness_check_keeps_limits_written_in_the_connection_string()
    {
        // Arrange
        var builder = Builder();
        builder.Services.AddDatabaseOptions();
        using var host = builder.Build();

        // Act
        var connection = Parse(host.Services.ReadinessConnectionString($"{Plain};Maximum Pool Size=3;Command Timeout=50"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(3);
            await Assert.That(connection.CommandTimeout).IsEqualTo(50);
        }
    }

    [Test]
    [Arguments("MaxPoolSize", "0")]
    [Arguments("MaxPoolSize", "1001")]
    [Arguments("CommandTimeout", "00:00:00")]
    [Arguments("CommandTimeout", "00:00:02.500")]
    [Arguments("StatementTimeout", "00:00:00")]
    [Arguments("StatementTimeout", "02:00:00")]
    [Arguments("IdleInTransactionSessionTimeout", "00:00:09.999")]
    public async Task An_invalid_limit_stops_the_host_at_startup_naming_the_key(string key, string value)
    {
        // Arrange
        var builder = Builder(($"Database:{key}", value));
        builder.Services.AddModuleDbContext<LimitsContext>(_ => Plain, (_, options, connection) => options.UseNpgsql(connection));
        using var host = builder.Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Assert
        await Assert.That(failure!.Message).Contains($"Configuration 'Database:{key}'");
    }

    [Test]
    public async Task Registering_two_module_contexts_validates_the_limits_once()
    {
        // Arrange
        var builder = Builder(("Database:MaxPoolSize", "0"));
        builder.Services.AddModuleDbContext<LimitsContext>(_ => Plain, (_, options, connection) => options.UseNpgsql(connection));
        builder.Services.AddModuleDbContext<OtherLimitsContext>(_ => Plain, (_, options, connection) => options.UseNpgsql(connection));
        using var host = builder.Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Assert
        await Assert.That(failure!.Failures.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task The_limits_bind_and_validate_when_another_validator_of_them_is_already_registered()
    {
        // Arrange
        var bound = Builder(("Database:MaxPoolSize", "9"), ("Database:CommandTimeout", "00:00:20"));
        bound.Services.AddSingleton<IValidateOptions<DatabaseOptions>, AcceptingValidator>();
        bound.Services.AddDatabaseOptions();
        using var boundHost = bound.Build();
        var invalid = Builder(("Database:MaxPoolSize", "0"));
        invalid.Services.AddSingleton<IValidateOptions<DatabaseOptions>, AcceptingValidator>();
        invalid.Services.AddDatabaseOptions();
        using var invalidHost = invalid.Build();

        // Act
        var limits = boundHost.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => invalidHost.StartAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(limits.MaxPoolSize).IsEqualTo(9);
            await Assert.That(limits.CommandTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
            await Assert.That(failure!.Message).Contains("Configuration 'Database:MaxPoolSize'");
        }
    }

    private static HostApplicationBuilder Builder(params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)));
        return builder;
    }

    private static NpgsqlConnectionStringBuilder Parse(string connectionString) => [with(connectionString)];

    /// <summary>A validator of the limits that some other part of a host registered first; it accepts everything.</summary>
    private sealed class AcceptingValidator : IValidateOptions<DatabaseOptions>
    {
        public ValidateOptionsResult Validate(string? name, DatabaseOptions options) => ValidateOptionsResult.Success;
    }

    /// <summary>A module context registered through the platform, to read its connection limits.</summary>
    private sealed class LimitsContext(DbContextOptions<LimitsContext> options) : DbContext(options);

    /// <summary>A second module context, to check its limits are the same.</summary>
    private sealed class OtherLimitsContext(DbContextOptions<OtherLimitsContext> options) : DbContext(options);
}
