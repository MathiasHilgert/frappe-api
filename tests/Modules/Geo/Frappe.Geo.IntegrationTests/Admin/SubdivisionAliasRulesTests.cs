using System.Net;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.Admin;

/// <summary>
/// What a manual alias may be (S3, S12): a name one subdivision of the country is known by, addressable in a route,
/// and removed by the same name it was added with, ignoring case and accents.
/// </summary>
[ClassDataSource<ValkeyCache>(Shared = SharedType.PerTestSession)]
public sealed class SubdivisionAliasRulesTests(ValkeyCache valkey)
{
    private const string Aliases = "/v1/geo/subdivisions/AR-C/aliases";

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_alias_with_a_character_a_route_cannot_carry_is_refused_both_when_added_and_when_removed(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var slash = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "a/b" });
        var backslash = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "a\\b" });
        var percent = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "100%" });
        var dots = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = ".." });
        var removal = await SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/a%2Fb");
        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await Assert.That(Outcome(slash)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.subdivision_alias.invalid"));
        await Assert.That(slash.Body.GetProperty("errors")[0].GetProperty("message").GetString()).Contains("“a/b”");
        await Assert.That(Outcome(backslash)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.subdivision_alias.invalid"));
        await Assert.That(Outcome(percent)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.subdivision_alias.invalid"));
        await Assert.That(Outcome(dots)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.subdivision_alias.invalid"));
        await Assert.That(Outcome(removal)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.subdivision_alias.invalid"));
        await Assert.That(listed.Body.GetProperty("aliases").EnumerateArray().Count(a => a.GetProperty("origin").GetString() == "manual")).IsEqualTo(0);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_added_alias_is_located_by_an_absolute_path_that_removes_it(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var added = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "Bs. As. (Capital), Reina del Plata" });
        var removed = await SendAsync(database, valkey, HttpMethod.Delete, added.Location!);

        await Assert.That(added.Status).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(Uri.UnescapeDataString(added.Location!)).IsEqualTo(Aliases + "/Bs. As. (Capital), Reina del Plata");
        await Assert.That(removed.Status).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_alias_another_subdivision_of_the_country_is_known_by_is_refused_as_taken(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-X/aliases", new { alias = "La Docta" });

        var sourceAlias = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-H/aliases", new { alias = "caba" });
        var manualAlias = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-H/aliases", new { alias = "la docta" });
        var name = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-H/aliases", new { alias = "CORDOBA" });
        var codeSuffix = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-H/aliases", new { alias = "c" });
        var free = await SendAsync(database, valkey, HttpMethod.Post, "/v1/geo/subdivisions/AR-H/aliases", new { alias = "El Impenetrable" });

        await Assert.That(Outcome(sourceAlias)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.taken"));
        await Assert.That(sourceAlias.Body.GetProperty("errors")[0].GetProperty("message").GetString()).Contains("AR-C");
        await Assert.That(Outcome(manualAlias)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.taken"));
        await Assert.That(Outcome(name)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.taken"));
        await Assert.That(Outcome(codeSuffix)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.taken"));
        await Assert.That(free.Status).IsEqualTo(HttpStatusCode.Created);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_subdivision_own_name_is_refused_and_a_manual_alias_is_removed_ignoring_case_and_accents(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "La Reina del Plata" });

        var ownName = await SendAsync(database, valkey, HttpMethod.Post, Aliases, new { alias = "ciudad autonoma de buenos aires" });
        var removed = await SendAsync(database, valkey, HttpMethod.Delete, Aliases + "/" + Uri.EscapeDataString("la reina del PLATÁ"));
        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await Assert.That(Outcome(ownName)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.subdivision_alias.duplicate"));
        await Assert.That(removed.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(listed.Body.GetProperty("aliases").EnumerateArray().Count(a => a.GetProperty("origin").GetString() == "manual")).IsEqualTo(0);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_alias_list_is_never_stored_by_a_cache(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());

        var listed = await SendAsync(database, valkey, HttpMethod.Get, Aliases);

        await Assert.That(listed.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(listed.CacheControl).IsEqualTo("no-store");
        await Assert.That(listed.ETag).IsNull();
    }
}
