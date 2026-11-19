using Frappe.Observability.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frappe.Migrator;

/// <summary>
/// Everything the Migrator runs: telemetry, then <see cref="DatabaseMigration"/> once over <c>Modules.Databases</c>, and
/// the process exit code.
/// </summary>
public static class MigratorComposition
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddFrappeMigrator()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.AddFrappeTelemetry("frappe-migrator");
            return builder;
        }
    }

    extension(IHost host)
    {
        /// <summary>
        /// Runs the migration once and returns the exit code: 0 on success, 1 on any failure, the host failing to start
        /// included. The host is stopped exactly once, whatever happened; a failure to stop is logged and leaves the exit
        /// code of the migration.
        /// </summary>
        public async Task<int> RunMigrationAsync()
        {
            ArgumentNullException.ThrowIfNull(host);
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Frappe.Migrator");
            var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

            int exitCode;
            try
            {
                // Started so telemetry exports; stopped (flushing it) below, before the exit code is returned.
                await host.StartAsync();
                await DatabaseMigration.RunAsync(configuration, Modules.Databases, logger, stopping);
                exitCode = 0;
            }
            catch (Exception exception)
            {
                logger.LogCritical(exception, "Database migration failed");
                exitCode = 1;
            }

            try
            {
                await host.StopAsync();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The Migrator host did not stop cleanly");
            }

            return exitCode;
        }
    }
}
