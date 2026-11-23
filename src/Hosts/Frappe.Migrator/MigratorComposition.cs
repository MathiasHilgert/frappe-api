using Frappe.Observability.Hosting;
using Frappe.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frappe.Migrator;

/// <summary>
/// Everything the Migrator runs: telemetry, then <see cref="DatabaseMigration"/> once, and the process exit code. The modules it
/// sets up are the ones <see cref="Modules.All"/> lists; the scheduler's job store is always set up.
/// </summary>
public static partial class MigratorComposition
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Composes the Migrator with the modules of <see cref="Modules.All"/>.</summary>
        public IHostApplicationBuilder AddFrappeMigrator() => builder.AddFrappeMigrator(Modules.All);

        /// <summary>Composes the Migrator with <paramref name="modules"/>, which tests replace to prove the phases.</summary>
        public IHostApplicationBuilder AddFrappeMigrator(IReadOnlyCollection<IDatabaseModule> modules)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(modules);
            builder.AddFrappeTelemetry("frappe-migrator");
            builder.Services.AddSingleton(new MigratedModules(modules));
            return builder;
        }
    }

    extension(IHost host)
    {
        /// <summary>Runs the migration once and returns the exit code: 0 on success, 1 on any failure.</summary>
        public async Task<int> RunMigrationAsync()
        {
            ArgumentNullException.ThrowIfNull(host);

            // Started so telemetry exports; stopped (flushing it) before the exit code is returned.
            await host.StartAsync();
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var modules = host.Services.GetRequiredService<MigratedModules>().Modules;
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Frappe.Migrator");
            var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

            try
            {
                await DatabaseMigration.RunAsync(configuration, modules, logger, stopping);
                await host.StopAsync();
                return 0;
            }
            catch (Exception exception)
            {
                LogMigrationFailed(logger, exception);
                await host.StopAsync();
                return 1;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Database migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);

    /// <summary>The module databases a composed Migrator sets up.</summary>
    private sealed record MigratedModules(IReadOnlyCollection<IDatabaseModule> Modules);
}
