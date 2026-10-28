using Frappe.Migrations;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

public sealed class MigrationsRunnerTests(AppFixture app)
{
    [Fact]
    public async Task Migrating_a_migrated_database_applies_nothing()
    {
        IReadOnlyList<string> applied = await MigrationsRunner.MigrateAllAsync(
            app.OwnerConnectionString,
            TestContext.Current.CancellationToken
        );

        applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_context_keeps_its_migrations_history_in_its_own_schema()
    {
        await using (HistoryProbeContext probe = new(Options()))
        {
            probe.Database.SetConnectionString(app.OwnerConnectionString);
            await probe.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using NpgsqlConnection connection = new(app.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand history = new(
            $"""
            SELECT to_regclass('{HistoryProbeContext.Schema}.{FrappeDbContextOptionsBuilderExtensions.HistoryTable}') IS NOT NULL;
            DROP SCHEMA IF EXISTS {HistoryProbeContext.Schema} CASCADE;
            """,
            connection
        );
        bool exists =
            await history.ExecuteScalarAsync(TestContext.Current.CancellationToken) is true;

        exists.ShouldBeTrue();
    }

    private static DbContextOptions<HistoryProbeContext> Options()
    {
        DbContextOptionsBuilder<HistoryProbeContext> options = new();
        options.UseFrappeDatabase(dataSource: null, HistoryProbeContext.Schema);
        return options.Options;
    }

    // A second context with no tables and no migrations, as a module starts.
    private sealed class HistoryProbeContext(DbContextOptions<HistoryProbeContext> options)
        : FrappeDbContext(options, Schema)
    {
        public const string Schema = "history_probe";
    }
}
