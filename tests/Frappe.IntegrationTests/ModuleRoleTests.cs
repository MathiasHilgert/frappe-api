using Npgsql;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>
/// The test hook of the per-module role plan (deploy/postgres/module-roles.sql): run against an empty database, the
/// module's role reads its own schema and is refused any other.
/// </summary>
public sealed class ModuleRoleTests(ApiFixture fixture)
{
    [Fact]
    public async Task ModuleRole_ReadsItsOwnSchema_AndIsDeniedAnotherModulesSchema()
    {
        var connectionString = await fixture.CreateDatabaseAsync("module_roles");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ExecuteAsync(dataSource, """
            CREATE SCHEMA identity; CREATE TABLE identity.probe (id int);
            CREATE SCHEMA tables; CREATE TABLE tables.probe (id int);
            """);
        await ExecuteAsync(dataSource, await File.ReadAllTextAsync(
            Path.Join(RepositoryRoot(), "deploy", "postgres", "module-roles.sql"), TestContext.Current.CancellationToken));

        await ExecuteAsync(dataSource, "SET ROLE frappe_identity; SELECT count(*) FROM identity.probe; RESET ROLE;");
        var denied = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(dataSource, "SET ROLE frappe_identity; SELECT count(*) FROM tables.probe;"));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "justfile")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
