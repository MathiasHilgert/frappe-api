using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Http.Sessions;
using Microsoft.AspNetCore.TestHost;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The caller's own sessions (S13, HTTP-08, HTTP-14): the paged list, revoking one (another principal's, an over one and a malformed id are the one
/// 404) and revoking every other, which keeps the session in use, with the origin check on a cookie request.
/// </summary>
public sealed class IdentityMySessionsRouteTests
{
    private const string Sessions = "/v1/identity/me/sessions";

    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<JsonDocument> BodyOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>The body with what is per request (the echoed id, the trace id) taken out, to compare answers.</summary>
    private static string Normalized(string body, string id) =>
        System.Text.RegularExpressions.Regex.Replace(
            body.Replace(id, "{id}", StringComparison.Ordinal), "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"",
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    private static string[] IdsOf(JsonDocument body) =>
        [.. body.RootElement.GetProperty("sessions").EnumerateArray().Select(s => s.GetProperty("id").GetString()!)];

    [Test]
    public async Task The_list_pages_the_callers_live_sessions_newest_first_and_never_shows_another_principals()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (principal, first, token) = host.Seed();
        var second = host.SeedSessionOf(principal, IdentitySessionHost.Start + Duration.FromMinutes(1), Chrome, IPNetwork.Parse("203.0.113.0/24"));
        var third = host.SeedSessionOf(principal, IdentitySessionHost.Start + Duration.FromMinutes(2));
        host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var one = await client.SendAsync(Request(HttpMethod.Get, $"{Sessions}?limit=2", token));
        using var oneBody = await BodyOf(one);
        var cursor = oneBody.RootElement.GetProperty("nextCursor").GetString();
        using var two = await client.SendAsync(Request(HttpMethod.Get, $"{Sessions}?limit=2&cursor={cursor}", token));
        using var twoBody = await BodyOf(two);

        // Assert
        var items = oneBody.RootElement.GetProperty("sessions");
        using (Assert.Multiple())
        {
            await Assert.That(one.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(one.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(IdsOf(oneBody)).IsEquivalentTo([third.Session.Id.ToString(), second.Session.Id.ToString()]);
            await Assert.That(items[1].GetProperty("browser").GetString()).IsEqualTo("Chrome");
            await Assert.That(items[1].GetProperty("os").GetString()).IsEqualTo("Windows");
            await Assert.That(items[1].GetProperty("ipPrefix").GetString()).IsEqualTo("203.0.113.0/24");
            await Assert.That(items[1].GetProperty("openedAt").GetDateTimeOffset()).IsEqualTo(second.Session.OpenedAt.ToDateTimeOffset());
            await Assert.That(items[1].GetProperty("lastSeenAt").GetDateTimeOffset()).IsEqualTo(second.Session.LastSeenAt.ToDateTimeOffset());
            await Assert.That(items[0].GetProperty("current").GetBoolean()).IsFalse();
            await Assert.That(two.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(IdsOf(twoBody)).IsEquivalentTo([first.Id.ToString()]);
            await Assert.That(twoBody.RootElement.GetProperty("sessions")[0].GetProperty("current").GetBoolean()).IsTrue();
            await Assert.That(twoBody.RootElement.TryGetProperty("nextCursor", out _)).IsFalse();
        }
    }

    [Test]
    [Arguments("limit=0", "platform.page_limit.out_of_range")]
    [Arguments("limit=201", "platform.page_limit.out_of_range")]
    [Arguments("limit=many", "platform.page_limit.invalid")]
    [Arguments("cursor=not-a-cursor", "platform.page_cursor.invalid")]
    public async Task A_limit_or_a_cursor_that_is_not_valid_is_a_400_with_its_code(string query, string code)
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, $"{Sessions}?{query}", token));

        // Assert
        using var body = await BodyOf(response);
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(code);
        }
    }

    [Test]
    public async Task A_cursor_of_another_principals_list_is_refused()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (mine, _, myToken) = host.Seed();
        var (other, _, otherToken) = host.Seed();
        host.SeedSessionOf(other, IdentitySessionHost.Start + Duration.FromMinutes(1));
        host.SeedSessionOf(mine, IdentitySessionHost.Start + Duration.FromMinutes(1));
        using var client = host.App.GetTestClient();
        using var theirs = await client.SendAsync(Request(HttpMethod.Get, $"{Sessions}?limit=1", otherToken));
        using var theirBody = await BodyOf(theirs);

        // Act
        using var response = await client.SendAsync(
            Request(HttpMethod.Get, $"{Sessions}?cursor={theirBody.RootElement.GetProperty("nextCursor").GetString()}", myToken));

        // Assert
        using var body = await BodyOf(response);
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("platform.page_cursor.invalid");
        }
    }

    [Test]
    public async Task Revoking_one_of_the_callers_sessions_answers_204_and_its_token_stops_working()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (principal, _, token) = host.Seed();
        var (session, otherToken) = host.SeedSessionOf(principal, IdentitySessionHost.Start + Duration.FromMinutes(1));
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Delete, $"{Sessions}/{session.Id}", token));
        using var after = await client.SendAsync(Request(HttpMethod.Get, "/v1/identity/sessions/current", otherToken));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(session.RevocationReason).IsEqualTo(RevocationReason.RevokedByHolder);
            await Assert.That(host.UnitOfWork.Commits).IsEqualTo(1);
            await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    public async Task A_foreign_a_missing_an_over_and_a_malformed_session_id_are_the_one_404_that_changes_nothing()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (principal, _, token) = host.Seed();
        var (foreign, _, _) = host.Seed();
        var foreignSession = host.SeedSessionOf(foreign, IdentitySessionHost.Start);
        var over = host.SeedSessionOf(principal, IdentitySessionHost.Start).Session;
        over.Revoke(RevocationReason.Logout, IdentitySessionHost.Start);
        using var client = host.App.GetTestClient();

        // Act
        var answers = new List<HttpResponseMessage>();
        var bodies = new List<string>();
        foreach (var id in new[] { foreignSession.Session.Id.ToString(), Guid.NewGuid().ToString(), over.Id.ToString(), "not-a-guid" })
        {
            var answer = await client.SendAsync(Request(HttpMethod.Delete, $"{Sessions}/{id}", token));
            bodies.Add(Normalized(await answer.Content.ReadAsStringAsync(), id));
            answers.Add(answer);
        }

        // Assert
        using (Assert.Multiple())
        {
            foreach (var answer in answers)
            {
                using (answer)
                {
                    using var body = JsonDocument.Parse(bodies[answers.IndexOf(answer)]);
                    await Assert.That(answer.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
                    await Assert.That(body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString())
                        .IsEqualTo("identity.session.not_found");
                }
            }

            await Assert.That(foreignSession.Session.RevokedAt).IsNull();
            await Assert.That(bodies[0]).IsEqualTo(bodies[1]);
            await Assert.That(bodies[2]).IsEqualTo(bodies[0]);
        }
    }

    [Test]
    public async Task Revoking_the_others_revokes_every_other_live_session_of_the_caller_and_keeps_the_current_and_other_principals()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (principal, current, token) = host.Seed();
        var one = host.SeedSessionOf(principal, IdentitySessionHost.Start + Duration.FromMinutes(1));
        var two = host.SeedSessionOf(principal, IdentitySessionHost.Start + Duration.FromMinutes(2));
        var (foreign, foreignSession, _) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Delete, $"{Sessions}/others", token));
        using var list = await client.SendAsync(Request(HttpMethod.Get, Sessions, token));

        // Assert
        using var body = await BodyOf(list);
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(one.Session.RevocationReason).IsEqualTo(RevocationReason.OthersRevoked);
            await Assert.That(two.Session.RevocationReason).IsEqualTo(RevocationReason.OthersRevoked);
            await Assert.That(current.RevokedAt).IsNull();
            await Assert.That(foreignSession.RevokedAt).IsNull();
            await Assert.That(IdsOf(body)).IsEquivalentTo([current.Id.ToString()]);
            await Assert.That(foreign.Id).IsNotEqualTo(principal.Id);
        }
    }

    [Test]
    public async Task Every_route_needs_a_session_and_a_cookie_delete_needs_a_console_origin()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();
        using var list = new HttpRequestMessage(HttpMethod.Get, Sessions);
        using var one = new HttpRequestMessage(HttpMethod.Delete, $"{Sessions}/{session.Id}");
        using var others = new HttpRequestMessage(HttpMethod.Delete, $"{Sessions}/others");
        using var cookie = new HttpRequestMessage(HttpMethod.Delete, $"{Sessions}/others");
        cookie.Headers.TryAddWithoutValidation("Cookie", $"{IdentitySessionCookie.Name}={token}");

        // Act
        var statuses = new[]
        {
            (await client.SendAsync(list)).StatusCode,
            (await client.SendAsync(one)).StatusCode,
            (await client.SendAsync(others)).StatusCode,
            (await client.SendAsync(cookie)).StatusCode,
        };

        // Assert
        await Assert.That(statuses).IsEquivalentTo(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden]);
    }
}
