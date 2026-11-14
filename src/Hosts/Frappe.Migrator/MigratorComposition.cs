using Frappe.Observability.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frappe.Migrator;

/// <summary>
/// Everything the Migrator runs: telemetry, then <see cref="DatabaseMigration"/> once, and the process exit code.
/// </summary>
public static class MigratorComposition
{
    public static IHostApplicationBuilder AddFrappeMigrator(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddFrappeTelemetry("frappe-migrator");
        return builder;
    }

    /// <summary>Runs the migration once and returns the exit code: 0 on success, 1 on any failure.</summary>
    public static async Task<int> RunMigrationAsync(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        // Started so telemetry exports; stopped (flushing it) before the exit code is returned.
        await host.StartAsync();
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Frappe.Migrator");
        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        try
        {
            await DatabaseMigration.RunAsync(configuration, logger, stopping);
            await host.StopAsync();
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Database migration failed");
            await host.StopAsync();
            return 1;
        }
    }
}
