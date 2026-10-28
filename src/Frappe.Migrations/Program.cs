using Microsoft.Extensions.Logging;

namespace Frappe.Migrations;

// Applies every pending migration of every context, then seeds every module's reference data, given the
// owner's connection string. A named class,
// not top-level statements, so its Program never collides with the host's in a test project that
// references both. Nothing it logs carries the connection string.
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using ILoggerFactory loggers = LoggerFactory.Create(static logging =>
            logging.AddSimpleConsole(static console => console.SingleLine = true)
        );
        ILogger logger = loggers.CreateLogger(typeof(Program).Namespace ?? "");

        if (args is not ["--connection", { Length: > 0 } connectionString])
        {
            Log.Usage(logger);
            return 2;
        }

        try
        {
            IReadOnlyList<string> applied = await MigrationsRunner.MigrateAllAsync(
                connectionString,
                CancellationToken.None
            );
            Log.Applied(logger, applied.Count, string.Join(", ", applied));
            int seeded = await MigrationsRunner.SeedAllAsync(
                connectionString,
                MigrationsRunner.Modules,
                CancellationToken.None
            );
            Log.Seeded(logger, seeded);
            return 0;
        }
#pragma warning disable CA1031 // The process boundary: any failure becomes exit code 1, never a crash.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Log.Failed(logger, exception);
            return 1;
        }
    }
}
