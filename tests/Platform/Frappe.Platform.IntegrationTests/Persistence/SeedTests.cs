using System.Data.Common;
using Frappe.Migrations;
using Frappe.Platform.IntegrationTests.Modularity;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

public sealed class SeedTests(AppFixture app)
{
    private readonly string _schema = $"seed_{Guid.CreateVersion7():N}";

    [Fact]
    public async Task Seeding_twice_changes_rows_only_the_first_time()
    {
        await CreateColoursAsync();
        // PingModule has no reference data, so it seeds nothing.
        IModule[] modules = [new ColourModule(_schema, "red"), new PingModule()];

        int first = await MigrationsRunner.SeedAllAsync(
            app.OwnerConnectionString,
            modules,
            TestContext.Current.CancellationToken
        );
        int second = await MigrationsRunner.SeedAllAsync(
            app.OwnerConnectionString,
            modules,
            TestContext.Current.CancellationToken
        );

        first.ShouldBe(1);
        second.ShouldBe(0);
        (await ColoursAsync()).ShouldBe(["red"]);
    }

    [Fact]
    public async Task A_failing_module_rolls_back_its_rows_and_the_modules_before_it_keep_theirs()
    {
        await CreateColoursAsync();
        IModule[] modules =
        [
            new ColourModule(_schema, "red"),
            new ColourModule(_schema, "blue", fails: true),
        ];

        await Should.ThrowAsync<InvalidOperationException>(() =>
            MigrationsRunner.SeedAllAsync(
                app.OwnerConnectionString,
                modules,
                TestContext.Current.CancellationToken
            )
        );

        (await ColoursAsync()).ShouldBe(["red"]);
    }

    private async Task CreateColoursAsync()
    {
        await using var owner = NpgsqlDataSource.Create(app.OwnerConnectionString);
        await using NpgsqlCommand create = owner.CreateCommand(
            $"CREATE SCHEMA {_schema}; CREATE TABLE {_schema}.colours (code text PRIMARY KEY);"
        );
        await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<string>> ColoursAsync()
    {
        await using var owner = NpgsqlDataSource.Create(app.OwnerConnectionString);
        await using NpgsqlCommand select = owner.CreateCommand(
            $"SELECT code FROM {_schema}.colours ORDER BY code"
        );
        await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(
            TestContext.Current.CancellationToken
        );
        List<string> codes = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            codes.Add(reader.GetString(0));
        }

        return codes;
    }

    // A module whose reference data is one colour, upserted the way IModule.SeedAsync asks; with fails it throws
    // after writing, as a seed that breaks halfway does.
    private sealed class ColourModule(string schema, string code, bool fails = false) : IModule
    {
        public ModuleMetadata Metadata { get; } =
            new("colour", "colour", "colours", new EventIdRange(1000, 1999), [], [], []);

        public void AddServices(IHostApplicationBuilder builder) { }

        public void MapEndpoints(RouteGroupBuilder group) { }

        public async Task<int> SeedAsync(
            DbConnection connection,
            CancellationToken cancellationToken
        )
        {
            await using DbCommand upsert = connection.CreateCommand();
            upsert.CommandText =
                $"INSERT INTO {schema}.colours (code) VALUES ('{code}') ON CONFLICT (code) DO NOTHING";
            int changed = await upsert.ExecuteNonQueryAsync(cancellationToken);
            return fails ? throw new InvalidOperationException("The seed broke halfway.") : changed;
        }
    }
}
