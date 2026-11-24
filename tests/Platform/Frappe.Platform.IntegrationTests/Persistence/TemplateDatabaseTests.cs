using Frappe.Testing.Containers;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// A template database is migrated once per session and every test clones it: the clone has the migrated schema, the clones
/// do not see each other, and concurrent callers share one migration.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class TemplateDatabaseTests(PostgresServer server)
{
    private const string Migrated = "CREATE SCHEMA clones; CREATE TABLE clones.items (id integer PRIMARY KEY); INSERT INTO clones.items VALUES (1)";

    [Test]
    public async Task A_clone_has_the_schema_the_template_was_migrated_with()
    {
        // Arrange
        var key = ScratchDatabase.Unique("clone_schema");
        object? rows = null;

        // Act
        await TemplateDatabase.RunAsync(
            server, key, owner => ScratchDatabase.ExecuteAsync(owner, Migrated), [], async owner =>
            {
                rows = await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM clones.items");
            });

        // Assert
        await Assert.That(rows).IsEqualTo(1L);
    }

    [Test]
    public async Task Two_clones_of_one_template_are_isolated_from_each_other()
    {
        // Arrange
        var key = ScratchDatabase.Unique("clone_isolation");
        Task Migrate(string owner) => ScratchDatabase.ExecuteAsync(owner, Migrated);
        object? other = null;

        // Act
        await TemplateDatabase.RunAsync(server, key, Migrate, [], async first =>
        {
            await ScratchDatabase.ExecuteAsync(first, "INSERT INTO clones.items VALUES (2)");
            await TemplateDatabase.RunAsync(
                server, key, Migrate, [], async second => other = await ScratchDatabase.ScalarAsync(second, "SELECT count(*) FROM clones.items"));
        });

        // Assert
        await Assert.That(other).IsEqualTo(1L);
    }

    [Test]
    public async Task The_migration_runs_once_for_concurrent_callers()
    {
        // Arrange
        var key = ScratchDatabase.Unique("clone_once");
        var migrations = 0;
        Task Migrate(string owner)
        {
            Interlocked.Increment(ref migrations);
            return ScratchDatabase.ExecuteAsync(owner, Migrated);
        }

        // Act
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => TemplateDatabase.RunAsync(server, key, Migrate, [], _ => Task.CompletedTask)));

        // Assert
        await Assert.That(migrations).IsEqualTo(1);
    }

    [Test]
    public async Task The_roles_the_migration_needs_exist_before_it_runs_and_a_clone_is_dropped_afterwards()
    {
        // Arrange
        var key = ScratchDatabase.Unique("clone_roles");
        var role = ScratchDatabase.Unique("tpl_role");
        string? cloneName = null;

        // Act
        await TemplateDatabase.RunAsync(
            server,
            key,
            owner => ScratchDatabase.ExecuteAsync(owner, $"CREATE SCHEMA s AUTHORIZATION {role}"),
            [role],
            async owner => cloneName = (string?)await ScratchDatabase.ScalarAsync(owner, "SELECT current_database()"));
        var survivors = await ScratchDatabase.ScalarAsync(server.ConnectionString, $"SELECT count(*) FROM pg_database WHERE datname = '{cloneName}'");

        // Assert
        await Assert.That(survivors).IsEqualTo(0L);
    }
}
