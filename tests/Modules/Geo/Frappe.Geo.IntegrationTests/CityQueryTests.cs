using Alba;
using Frappe.Geo.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// Cities are queried in the database: one statement per request, whatever the page or match count, on the indexes.
public sealed class CityQueryTests(GeoFixture geo)
{
    [Fact]
    public async Task A_page_costs_the_same_commands_for_one_city_or_twenty()
    {
        int one = await CommandsForAsync("/v1/geo/cities?limit=1");
        int twenty = await CommandsForAsync("/v1/geo/cities?limit=20");

        one.ShouldBe(1);
        twenty.ShouldBe(one);
    }

    [Theory]
    [InlineData("pi")]
    [InlineData("pilar")]
    [InlineData("san")]
    [InlineData("pilar cordoba")]
    public async Task A_search_sends_one_command_whatever_it_matches(string q)
    {
        (await CommandsForAsync($"/v1/geo/cities?q={Uri.EscapeDataString(q)}")).ShouldBe(1);
    }

    [Fact]
    public async Task A_search_uses_word_similarity_and_like_on_the_search_columns()
    {
        RecordedCommand search = await SearchCommandAsync("pilar cordoba");

        search.Sql.ShouldContain("<%");
        search.Sql.ShouldContain("word_similarity(");
        search.Sql.ShouldContain("c.search_text LIKE");
        search.Sql.ShouldContain("c.search_name LIKE");
        search.Sql.ShouldContain("LIMIT");
        search.Sql.ShouldNotContain("strpos");
    }

    [Fact]
    public async Task A_search_can_use_the_trigram_index()
    {
        string plan = await PlanAsync(await SearchCommandAsync("pilar cordoba"));

        plan.ShouldContain("ix_cities_search_text_trgm");
    }

    [Fact]
    public async Task A_two_character_search_can_use_the_name_btree()
    {
        RecordedCommand search = await SearchCommandAsync("pi");

        search.Sql.ShouldNotContain("<%");
        (await PlanAsync(search)).ShouldContain("ix_cities_search");
    }

    private async Task<int> CommandsForAsync(string url)
    {
        CommandCounter counter = new();
        await using IAlbaHost host = await HostAsync(counter);
        int afterStart = counter.Count;

        await GeoHttp.GetAsync(host, url, 200);

        return counter.Count - afterStart;
    }

    private async Task<RecordedCommand> SearchCommandAsync(string q)
    {
        CommandCounter counter = new();
        await using IAlbaHost host = await HostAsync(counter);
        int afterStart = counter.Readers.Count;

        await GeoHttp.GetAsync(host, $"/v1/geo/cities?q={Uri.EscapeDataString(q)}", 200);

        return counter.Readers[afterStart];
    }

    private Task<IAlbaHost> HostAsync(CommandCounter counter) =>
        geo.StartHostAsync(builder =>
            builder.Services.ConfigureDbContext<GeoDbContext>(options =>
                options.AddInterceptors(counter)
            )
        );

    // With sequential scans priced out, the plan shows the index the planner can use; on ~4,000 rows it would
    // otherwise prefer a scan, which says nothing about the query shape.
    private async Task<string> PlanAsync(RecordedCommand command)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            cancellationToken
        );
        await using (NpgsqlCommand off = new("SET LOCAL enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync(cancellationToken);
        }

        await using NpgsqlCommand explain = new($"EXPLAIN {command.Sql}", connection);
        foreach ((string name, object? value) in command.Parameters)
        {
            explain.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        List<string> lines = [];
        await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(reader.GetString(0));
            }
        }

        await transaction.RollbackAsync(cancellationToken);
        return string.Join('\n', lines);
    }
}
