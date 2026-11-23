using Frappe.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Platform.UnitTests.Scheduling;

/// <summary>The scheduler's job store connects under the same limits as the module databases.</summary>
public sealed class SchedulerConnectionLimitsTests
{
    private const string Scheduler = "Host=db;Database=frappe;Username=frappe_scheduler;Password=secret";

    [Test]
    public async Task The_job_store_gets_the_configured_pool_size_and_the_default_command_timeout()
    {
        // Arrange
        await using var services = Configured(Scheduler, ("Database:MaxPoolSize", "4"));

        // Act
        var connection = JobStore(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(4);
            await Assert.That(connection.CommandTimeout).IsEqualTo(35);
        }
    }

    [Test]
    public async Task Limits_written_in_the_scheduler_connection_string_win()
    {
        // Arrange
        await using var services = Configured($"{Scheduler};Maximum Pool Size=15;Command Timeout=60", ("Database:MaxPoolSize", "4"));

        // Act
        var connection = JobStore(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(15);
            await Assert.That(connection.CommandTimeout).IsEqualTo(60);
        }
    }

    [Test]
    public async Task A_scheduler_given_in_code_gets_the_default_limits()
    {
        // Arrange
        await using var services = new ServiceCollection().AddFrappeScheduling(Scheduler, _ => { }).BuildServiceProvider();

        // Act
        var connection = JobStore(services);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(connection.MaxPoolSize).IsEqualTo(6);
            await Assert.That(connection.CommandTimeout).IsEqualTo(35);
        }
    }

    private static ServiceProvider Configured(string connectionString, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([KeyValuePair.Create("ConnectionStrings:Scheduler", (string?)connectionString),
                .. settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value))])
            .Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddFrappeScheduling(_ => { })
            .BuildServiceProvider();
    }

    private static NpgsqlConnectionStringBuilder JobStore(IServiceProvider services) =>
        [with(services.GetRequiredKeyedService<NpgsqlDataSource>(SchedulingRegistration.DataSourceKey).ConnectionString)];
}
