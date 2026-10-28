using System.Reflection;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Frappe.Migrations.UnitTests;

// The migrations check: every context's model, its migrations and its schema agree. None of these
// tests opens a connection.
public sealed class ContextModelTests
{
    [Fact]
    public void Platform_is_migrated_first()
    {
        using FrappeDbContext first = MigrationsRunner.Contexts[0].CreateDbContext([]);

        first.ShouldBeOfType<PlatformDbContext>();
    }

    [Fact]
    public void No_context_has_model_changes_that_no_migration_captures()
    {
        string[] pending =
        [
            .. Contexts()
                .Where(static db => db.Database.HasPendingModelChanges())
                .Select(static db => db.GetType().Name),
        ];

        pending.ShouldBeEmpty();
    }

    [Fact]
    public void Every_concrete_context_is_migrated_by_the_runner()
    {
        string[] listed = [.. Contexts().Select(static db => db.GetType().FullName ?? "")];

        string[] concrete =
        [
            .. Directory
                .EnumerateFiles(AppContext.BaseDirectory, "Frappe.*.dll")
                .Select(static path => Assembly.Load(AssemblyName.GetAssemblyName(path)))
                .SelectMany(static assembly => assembly.GetTypes())
                .Where(static type =>
                    type is { IsAbstract: false } && type.IsSubclassOf(typeof(FrappeDbContext))
                )
                .Select(static type => type.FullName ?? ""),
        ];

        concrete.ShouldNotBeEmpty();
        listed.ShouldBe(concrete, ignoreOrder: true);
    }

    [Fact]
    public void Each_context_keeps_its_tables_and_its_history_in_its_own_schema()
    {
        foreach (FrappeDbContext db in Contexts())
        {
            string? schema = db.Model.GetDefaultSchema();
            string? historySchema = db.GetService<IDbContextOptions>()
                .Extensions.OfType<RelationalOptionsExtension>()
                .Single()
                .MigrationsHistoryTableSchema;

            schema.ShouldNotBeNullOrEmpty();
            historySchema.ShouldBe(schema, db.GetType().Name);
            db.Model.GetEntityTypes()
                .Where(static type => type.GetTableName() is not null)
                .Select(static type => type.GetSchema())
                .ShouldAllBe(tableSchema => tableSchema == schema, db.GetType().Name);
        }
    }

    // shortcut: the contexts are never disposed; they hold no connection and the process is short.
    private static IEnumerable<FrappeDbContext> Contexts() =>
        MigrationsRunner.Contexts.Select(static factory => factory.CreateDbContext([]));
}
