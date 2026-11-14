using System.Net;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Npgsql;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.Admin;

/// <summary>Subdivision aliases (S12): the source's and the manual ones, which the territory sync never touches.</summary>
[ClassDataSource<ValkeyCache>(Shared = SharedType.PerTestSession)]
public sealed class SubdivisionAliasTests(ValkeyCache valkey)
{
    private const string Aliases = "/v1/geo/subdivisions/AR-C/aliases";

    private static string[] Names(GeoApi.Answer answer, string origin) =>
        [.. answer.Body.GetProperty("aliases").EnumerateArray().Where(a => a.GetProperty("origin").GetString() == origin).Select(a => a.GetProperty("alias").GetString()!)];

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_manual_alias_survives_a_sync_that_changes_the_territory_and_is_served_with_the_source_ones(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);

        var added = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" });
        georef.Replace("provincias.json", json => json.Replace("-58.445876325", "-58.445876", StringComparison.Ordinal));
        georef.Replace("localidades.json", json => json.Replace("\"Villa Carlos Paz\"", "\"Villa Carlos Paz (renamed)\"", StringComparison.Ordinal));
        await SourceSync.TerritoryAsync(database, georef);

        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);
        var subdivision = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/subdivisions/AR-C");
        await Assert.That(added.Status).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(Names(listed, "manual")).IsEquivalentTo(["La Reina del Plata"]);
        await Assert.That(Names(listed, "source")).Contains("CABA");
        await Assert.That(subdivision.Body.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!)).Contains("La Reina del Plata");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_alias_the_subdivision_already_has_is_refused_as_a_duplicate(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var sourceOne = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "caba" });
        await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" });
        var manualOne = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" });

        await Assert.That(Outcome(sourceOne)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.duplicate"));
        await Assert.That(Outcome(manualOne)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.duplicate"));
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task Only_a_manual_alias_is_removed_and_an_unknown_subdivision_or_empty_alias_is_refused(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" });

        var removed = await SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/" + Uri.EscapeDataString("La Reina del Plata"));
        var missing = await SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/" + Uri.EscapeDataString("La Reina del Plata"));
        var sourceOne = await SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/CABA");
        var unknown = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-Q/aliases", new { alias = "Algo" });
        var empty = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "  " });
        var absent = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { });
        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await Assert.That(removed.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Outcome(missing)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.subdivision_alias.not_found"));
        await Assert.That(Outcome(sourceOne)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.imported"));
        await Assert.That(Outcome(unknown)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.subdivision.not_found"));
        await Assert.That(Outcome(empty)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.parameter.required"));
        await Assert.That(Outcome(absent)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.parameter.required"));
        await Assert.That(absent.Body.GetProperty("errors")[0].GetProperty("parameters").GetProperty("parameter").GetString()).IsEqualTo("alias");
        await Assert.That(Names(listed, "manual")).IsEmpty();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_removal_that_loses_a_race_with_another_removal_of_the_same_alias_answers_not_found(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" });

        var answer = await RaceAsync(database, "DELETE FROM geo.subdivision_aliases WHERE subdivision_code = 'AR-C' AND alias = 'La Reina del Plata'",
            () => SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/" + Uri.EscapeDataString("La Reina del Plata")));

        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);
        await Assert.That(Outcome(answer)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.subdivision_alias.not_found"));
        await Assert.That(Names(listed, "manual")).IsEmpty();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_addition_that_loses_a_race_with_another_addition_of_the_same_alias_is_refused_as_a_duplicate(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var answer = await RaceAsync(database, "INSERT INTO geo.subdivision_aliases (subdivision_code, alias, search_form, manual) VALUES ('AR-C', 'La Reina del Plata', 'la reina del plata', true)",
            () => SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" }));

        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);
        await Assert.That(Outcome(answer)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.duplicate"));
        await Assert.That(Names(listed, "manual")).IsEquivalentTo(["La Reina del Plata"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_addition_that_loses_a_race_with_another_addition_differing_only_by_case_is_refused_as_a_duplicate(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var answer = await RaceAsync(database, "INSERT INTO geo.subdivision_aliases (subdivision_code, alias, search_form, manual) VALUES ('AR-C', 'Foo', 'foo', true)",
            () => SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "foo" }));

        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);
        await Assert.That(Outcome(answer)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.duplicate"));
        await Assert.That(Names(listed, "manual")).IsEquivalentTo(["Foo"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_addition_that_loses_a_race_with_the_same_alias_added_to_another_subdivision_is_refused_as_taken(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var answer = await RaceAsync(database, "INSERT INTO geo.subdivision_aliases (subdivision_code, alias, search_form, manual) VALUES ('AR-X', 'La Docta', 'la docta', true)",
            () => SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-H/aliases", new { alias = "la docta" }));

        var stored = await database.QueryAsync("SELECT subdivision_code || ':' || alias FROM geo.subdivision_aliases WHERE search_form = 'la docta'", reader => reader.GetString(0));
        await Assert.That(Outcome(answer)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.taken"));
        await Assert.That(answer.Body.GetProperty("errors")[0].GetProperty("message").GetString()).Contains("AR-X");
        await Assert.That(stored).IsEquivalentTo(["AR-X:La Docta"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_case_only_race_on_one_subdivision_is_a_duplicate_even_when_the_country_index_is_checked_first(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        // Postgres checks unique indexes in creation order: recreating the subdivision's index puts the country's first.
        await database.QueryAsync("""
            DROP INDEX geo.ux_subdivision_aliases_subdivision_search_form;
            CREATE UNIQUE INDEX ux_subdivision_aliases_subdivision_search_form ON geo.subdivision_aliases (subdivision_code, search_form);
            SELECT 1
            """, reader => reader.GetInt32(0));
        var answer = await RaceAsync(database, "INSERT INTO geo.subdivision_aliases (subdivision_code, alias, search_form, manual) VALUES ('AR-C', 'Foo', 'foo', true)",
            () => SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "foo" }));

        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);
        await Assert.That(Outcome(answer)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.duplicate"));
        await Assert.That(Names(listed, "manual")).IsEquivalentTo(["Foo"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_retired_subdivision_s_manual_alias_blocks_another_subdivision_until_it_is_removed(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await database.QueryAsync("""
            UPDATE geo.subdivisions SET retired_at = now() WHERE code = 'AR-H';
            INSERT INTO geo.subdivision_aliases (subdivision_code, alias, search_form, manual) VALUES ('AR-H', 'La Docta', 'la docta', true) RETURNING 1
            """, reader => reader.GetInt32(0));

        var blocked = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-X/aliases", new { alias = "la docta" });
        var removed = await SendAsync(database, valkey, HttpMethod.Delete, "/v1/geo/subdivisions/AR-H/aliases/" + Uri.EscapeDataString("La Docta"));
        var added = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-X/aliases", new { alias = "la docta" });

        var stored = await database.QueryAsync("SELECT subdivision_code || ':' || alias FROM geo.subdivision_aliases WHERE search_form = 'la docta'", reader => reader.GetString(0));
        await Assert.That(Outcome(blocked)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.taken"));
        await Assert.That(removed.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(added.Status).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(stored).IsEquivalentTo(["AR-X:la docta"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_source_alias_another_subdivision_has_by_hand_is_still_imported_by_the_sync(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        await database.QueryAsync("""
            DELETE FROM geo.subdivision_aliases WHERE subdivision_code = 'AR-C' AND alias = 'CABA';
            INSERT INTO geo.subdivision_aliases (subdivision_code, alias, search_form, manual) VALUES ('AR-H', 'Caba', 'caba', true) RETURNING 1
            """, reader => reader.GetInt32(0));

        ChangeTheSource(georef, "-58.445876325", "-58.445876");
        var outcome = await SourceSync.TerritoryAsync(database, georef);

        var stored = await database.QueryAsync("SELECT subdivision_code || ':' || alias || ':' || manual FROM geo.subdivision_aliases WHERE search_form = 'caba'", reader => reader.GetString(0));
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
        await Assert.That(stored).IsEquivalentTo(["AR-C:CABA:false", "AR-H:Caba:true"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_source_alias_equal_to_a_manual_one_ignoring_case_and_accents_is_skipped_until_the_manual_one_is_removed(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        await database.QueryAsync("DELETE FROM geo.subdivision_aliases WHERE subdivision_code = 'AR-C' AND alias = 'CABA' RETURNING 1", reader => reader.GetInt32(0));
        var added = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "cabá" });

        ChangeTheSource(georef, "-58.445876325", "-58.445876");
        await SourceSync.TerritoryAsync(database, georef);
        var whileManual = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/" + Uri.EscapeDataString("cabá"));
        ChangeTheSource(georef, "-58.445876", "-58.4458");
        await SourceSync.TerritoryAsync(database, georef);
        var afterRemoval = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await Assert.That(added.Status).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(Names(whileManual, "manual")).IsEquivalentTo(["cabá"]);
        await Assert.That(Names(whileManual, "source")).DoesNotContain("CABA");
        await Assert.That(Names(afterRemoval, "manual")).IsEmpty();
        await Assert.That(Names(afterRemoval, "source")).Contains("CABA");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_source_alias_whose_spelling_changes_but_not_its_search_form_is_replaced_by_the_sync(GeoDatabase database)
    {
        var georef = new SourceServer();
        await SourceSync.TerritoryAsync(database, georef);
        await database.QueryAsync("UPDATE geo.subdivision_aliases SET alias = 'Caba' WHERE subdivision_code = 'AR-C' AND alias = 'CABA' RETURNING 1", reader => reader.GetInt32(0));

        ChangeTheSource(georef, "-58.445876325", "-58.445876");
        await SourceSync.TerritoryAsync(database, georef);
        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await Assert.That(Names(listed, "source")).Contains("CABA");
        await Assert.That(Names(listed, "source")).DoesNotContain("Caba");
    }

    /// <summary>Moves AR-C's centroid so the next territory sync applies a changed snapshot instead of answering unchanged.</summary>
    private static void ChangeTheSource(SourceServer georef, string longitude, string changed) =>
        georef.Replace("provincias.json", json => json.Replace(longitude, changed, StringComparison.Ordinal));

    /// <summary>Another writer changes the alias first and holds its row lock until the request waits on it.</summary>
    private static async Task<GeoApi.Answer> RaceAsync(GeoDatabase database, string otherWriter, Func<Task<GeoApi.Answer>> request)
    {
        await using var other = new NpgsqlConnection(database.ConnectionString);
        await other.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await using (var change = new NpgsqlCommand(otherWriter, other, transaction))
        {
            await change.ExecuteNonQueryAsync();
        }

        var answer = request();
        await WaitForALockWaitAsync(database, answer);
        await transaction.CommitAsync();
        return await answer;
    }
}
