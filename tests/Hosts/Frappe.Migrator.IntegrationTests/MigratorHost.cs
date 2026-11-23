using Frappe.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Frappe.Migrator.IntegrationTests;

/// <summary>The Migrator composed as deployed (Production, no defaults) with in-memory settings, run through its own entry point.</summary>
internal static class MigratorHost
{
    public static IHost Build(Dictionary<string, string?> settings, IReadOnlyCollection<IDatabaseModule>? modules = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        _ = builder.AddFrappeMigrator(modules ?? []);
        return builder.Build();
    }

    /// <summary>The settings of a run against <paramref name="owner"/>: its connection string and the scheduler role's password.</summary>
    public static Dictionary<string, string?> Settings(string owner, string schedulerPassword) => new()
    {
        ["ConnectionStrings:Owner"] = owner,
        ["Roles:Scheduler:Password"] = schedulerPassword,
    };

    public static async Task<int> RunAsync(Dictionary<string, string?> settings, IReadOnlyCollection<IDatabaseModule>? modules = null)
    {
        using var host = Build(settings, modules);
        return await host.RunMigrationAsync();
    }
}
