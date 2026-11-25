using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Http.Sessions;
using Frappe.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The session routes and what authenticates them (S8, S9, SEC-01 to SEC-03): the token as a bearer header or the console cookie, bearer first,
/// a duplicated cookie refused, every refusal the one 401 with <c>WWW-Authenticate</c>, the origin check on a cookie logout, and the cookie cleared.
/// </summary>
public sealed class IdentitySessionRouteTests
{
    private const string Current = "/v1/identity/sessions/current";

    private static HttpRequestMessage Request(HttpMethod method, string path, string? bearer = null, string[]? cookies = null, string? origin = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        }

        foreach (var cookie in cookies ?? [])
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return request;
    }

    private static string Cookie(string token) => $"{IdentitySessionCookie.Name}={token}";

    private static async Task<(string? Code, string? Message)> ProblemOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = problem.RootElement.GetProperty("errors")[0];
        return (error.GetProperty("code").GetString(), error.TryGetProperty("message", out var message) ? message.GetString() : null);
    }

    private static async Task AssertInvalidSessionAsync(HttpResponseMessage response)
    {
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(response.Headers.WwwAuthenticate.ToString()).IsEqualTo("Bearer");
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            await Assert.That((await ProblemOf(response)).Code).IsEqualTo("identity.session.invalid");
        }
    }

    [Test]
    public async Task A_bearer_token_authenticates_and_the_current_session_is_served_uncached()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(session.Id);
            await Assert.That(body.RootElement.GetProperty("idleExpiresAt").GetDateTimeOffset()).IsEqualTo(session.IdleExpiresAt.ToDateTimeOffset());
            await Assert.That(body.RootElement.GetProperty("absoluteExpiresAt").GetDateTimeOffset())
                .IsEqualTo(session.AbsoluteExpiresAt.ToDateTimeOffset());
            await Assert.That(body.RootElement.TryGetProperty("stepUpValidUntil", out var stepUp) && stepUp.ValueKind == JsonValueKind.Null).IsTrue();
        }
    }

    [Test]
    public async Task A_head_request_with_a_bearer_token_answers_like_the_get_without_a_body()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Head, Current, bearer: token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await response.Content.ReadAsStringAsync()).Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task The_console_cookie_authenticates_a_safe_request_without_an_origin()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, Current, cookies: [Cookie(token)]));

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(session.Id);
        }
    }

    [Test]
    public async Task A_request_without_a_session_is_the_one_401_with_a_challenge_in_the_callers_language()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        using var client = host.App.GetTestClient();
        using var spanish = Request(HttpMethod.Get, Current);
        spanish.Headers.TryAddWithoutValidation("Accept-Language", "es");
        using var portuguese = Request(HttpMethod.Get, Current);
        portuguese.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR");

        // Act
        using var english = await client.SendAsync(Request(HttpMethod.Get, Current));
        using var inSpanish = await client.SendAsync(spanish);
        using var inPortuguese = await client.SendAsync(portuguese);

        // Assert
        await AssertInvalidSessionAsync(english);
        using (Assert.Multiple())
        {
            await Assert.That((await ProblemOf(english)).Message).StartsWith("The session is missing");
            await Assert.That((await ProblemOf(inSpanish)).Message).StartsWith("La sesión falta");
            await Assert.That((await ProblemOf(inPortuguese)).Message).StartsWith("A sessão está ausente");
            await Assert.That(host.Store.Lookups).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_bearer_token_wins_over_a_cookie_and_a_bad_bearer_is_not_rescued_by_a_good_cookie()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, mine, myToken) = host.Seed();
        var (_, _, otherToken) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var bearerWins = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: myToken, cookies: [Cookie(otherToken)]));
        using var badBearer = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: "fs_nope", cookies: [Cookie(myToken)]));

        // Assert
        using var body = JsonDocument.Parse(await bearerWins.Content.ReadAsStringAsync());
        using (Assert.Multiple())
        {
            await Assert.That(bearerWins.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(mine.Id);
        }

        await AssertInvalidSessionAsync(badBearer);
    }

    [Test]
    public async Task A_session_cookie_sent_twice_never_authenticates_whether_it_comes_in_one_header_line_or_two()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, token) = host.Seed();
        var (_, _, other) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var oneLine = await client.SendAsync(Request(HttpMethod.Get, Current, cookies: [$"{Cookie(token)}; theme=dark; {Cookie(other)}"]));
        using var twoLines = await client.SendAsync(Request(HttpMethod.Get, Current, cookies: [Cookie(token), Cookie(token)]));

        // Assert
        await AssertInvalidSessionAsync(oneLine);
        await AssertInvalidSessionAsync(twoLines);
        await Assert.That(host.Store.Lookups).IsEqualTo(0);
    }

    [Test]
    public async Task A_text_that_is_not_a_token_is_refused_without_reading_any_session()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: "not-a-token"));
        using var empty = await client.SendAsync(Request(HttpMethod.Get, Current, cookies: [$"{IdentitySessionCookie.Name}="]));

        // Assert
        await AssertInvalidSessionAsync(response);
        await AssertInvalidSessionAsync(empty);
        await Assert.That(host.Store.Lookups).IsEqualTo(0);
    }

    [Test]
    public async Task A_token_nobody_holds_is_the_same_401()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        using var client = host.App.GetTestClient();
        var unknown = new Frappe.Identity.Infrastructure.Sessions.SessionTokens().Issue().Value;

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: unknown));

        // Assert
        await AssertInvalidSessionAsync(response);
        await Assert.That(host.Store.Lookups).IsEqualTo(1);
    }

    [Test]
    public async Task A_session_unused_for_twelve_hours_is_refused_and_one_used_within_that_keeps_going()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        host.Time.Advance(TimeSpan.FromHours(11));
        using var stillIn = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));
        host.Time.Advance(TimeSpan.FromHours(11));
        using var usedAgain = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));
        host.Time.Advance(TimeSpan.FromHours(13));
        using var idledOut = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stillIn.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(usedAgain.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        await AssertInvalidSessionAsync(idledOut);
    }

    [Test]
    public async Task A_session_that_is_used_all_the_time_still_ends_thirty_days_after_it_opened()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        // Eleven-hour gaps keep the session from ever idling out; the last use is an hour before the thirty days are over.
        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromDays(30) - TimeSpan.FromHours(12); elapsed += TimeSpan.FromHours(11))
        {
            host.Time.Advance(TimeSpan.FromHours(11));
            session.Touch(Instant.FromDateTimeOffset(host.Time.GetUtcNow()), Duration.FromHours(12));
        }

        host.Time.Advance(TimeSpan.FromDays(30) - TimeSpan.FromHours(1) - (host.Time.GetUtcNow() - IdentitySessionHost.Start.ToDateTimeOffset()));
        using var lastHour = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));
        host.Time.Advance(TimeSpan.FromHours(1));
        using var ended = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        await Assert.That(lastHour.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertInvalidSessionAsync(ended);
    }

    [Test]
    public async Task A_session_opened_under_an_older_credential_stamp_and_one_of_a_pending_principal_are_refused()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, stale) = host.Seed(staleStamp: true);
        var (_, _, pending) = host.Seed(PrincipalStatus.Pending);
        using var client = host.App.GetTestClient();

        // Act
        using var staleResponse = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: stale));
        using var pendingResponse = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: pending));

        // Assert
        await AssertInvalidSessionAsync(staleResponse);
        await AssertInvalidSessionAsync(pendingResponse);
    }

    [Test]
    public async Task A_route_nobody_guards_never_reads_a_session_even_when_the_request_carries_one()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var withBearer = await client.SendAsync(Request(HttpMethod.Get, "/v1/probe/open", bearer: token));
        using var withBadBearer = await client.SendAsync(Request(HttpMethod.Get, "/v1/probe/open", bearer: "fs_nope"));
        using var withCookie = await client.SendAsync(Request(HttpMethod.Get, "/v1/probe/open", cookies: [Cookie(token), Cookie(token)]));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(withBearer.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(withBadBearer.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(withCookie.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(host.Store.Lookups).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Logging_out_with_the_cookie_needs_a_console_origin_before_anything_else_runs()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var noOrigin = await client.SendAsync(Request(HttpMethod.Delete, Current, cookies: [Cookie(token)]));
        using var foreign = await client.SendAsync(Request(HttpMethod.Delete, Current, cookies: [Cookie(token)], origin: "https://evil.example.com"));
        using var noCredentialAtAll = await client.SendAsync(Request(HttpMethod.Delete, Current, cookies: [Cookie("fs_nope")]));
        using var stillAlive = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(noOrigin.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That((await ProblemOf(noOrigin)).Code).IsEqualTo("http.origin.forbidden");
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That((await ProblemOf(foreign)).Code).IsEqualTo("http.origin.forbidden");
            await Assert.That(noCredentialAtAll.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(stillAlive.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(session.RevokedAt).IsNull();
        }
    }

    [Test]
    public async Task Logging_out_from_the_console_revokes_the_session_clears_the_cookie_and_the_token_stops_working()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var loggedOut = await client.SendAsync(
            Request(HttpMethod.Delete, Current, cookies: [Cookie(token)], origin: IdentitySessionHost.ConsoleOrigin));
        using var afterwards = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        var cleared = loggedOut.Headers.GetValues("Set-Cookie").Single();
        using (Assert.Multiple())
        {
            await Assert.That(loggedOut.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(loggedOut.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(cleared).StartsWith($"{IdentitySessionCookie.Name}=;");
            await Assert.That(cleared).Contains("expires=Thu, 01 Jan 1970");
            await Assert.That(cleared).Contains("path=/");
            await Assert.That(cleared).Contains("secure");
            await Assert.That(cleared).Contains("httponly");
            await Assert.That(cleared).Contains("samesite=lax");
            await Assert.That(cleared).DoesNotContain("domain");
            await Assert.That(session.RevocationReason).IsEqualTo(Frappe.Identity.Domain.Sessions.RevocationReason.Logout);
            await Assert.That(host.UnitOfWork.Commits).IsEqualTo(1);
        }

        await AssertInvalidSessionAsync(afterwards);
    }

    [Test]
    public async Task Logging_out_with_a_bearer_token_is_not_origin_checked_and_revokes_the_session()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var loggedOut = await client.SendAsync(Request(HttpMethod.Delete, Current, bearer: token));
        using var again = await client.SendAsync(Request(HttpMethod.Delete, Current, bearer: token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(loggedOut.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(session.RevokedAt).IsNotNull();
        }

        await AssertInvalidSessionAsync(again);
    }

    [Test]
    public async Task A_bearer_request_that_also_carries_a_cookie_is_judged_by_the_bearer_and_not_origin_checked()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var loggedOut = await client.SendAsync(Request(HttpMethod.Delete, Current, bearer: token, cookies: [Cookie("fs_other")]));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(loggedOut.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(session.RevokedAt).IsNotNull();
        }
    }

    [Test]
    public async Task A_request_is_carried_by_the_cookie_only_when_the_cookie_is_the_credential_it_is_judged_by()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var bare = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"theme=dark; {Cookie("fs_x")}";
        bare.Request.Headers.Cookie = "theme=dark";
        var withBearer = new DefaultHttpContext();
        withBearer.Request.Headers.Cookie = Cookie("fs_x");
        withBearer.Request.Headers.Authorization = "bearer fs_y";
        var withOtherScheme = new DefaultHttpContext();
        withOtherScheme.Request.Headers.Cookie = Cookie("fs_x");
        withOtherScheme.Request.Headers.Authorization = "Basic abc";

        // Act
        var carried = (context: IdentitySessionCookie.IsCarriedBy(context), bare: IdentitySessionCookie.IsCarriedBy(bare),
            bearer: IdentitySessionCookie.IsCarriedBy(withBearer), basic: IdentitySessionCookie.IsCarriedBy(withOtherScheme));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(carried.context).IsTrue();
            await Assert.That(carried.bare).IsFalse();
            await Assert.That(carried.bearer).IsFalse();
            await Assert.That(carried.basic).IsTrue();
        }
    }

    [Test]
    public async Task The_cookie_is_written_host_only_http_only_secure_and_lax_until_the_session_ends()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var until = new DateTimeOffset(2026, 12, 24, 12, 0, 0, TimeSpan.Zero);

        // Act
        IdentitySessionCookie.Append(context.Response, "fs_token", until);

        // Assert
        var header = context.Response.Headers.SetCookie.ToString();
        using (Assert.Multiple())
        {
            await Assert.That(header).StartsWith("__Host-frappe-session=fs_token;");
            await Assert.That(header).Contains("expires=Thu, 24 Dec 2026 12:00:00 GMT");
            await Assert.That(header).Contains("path=/");
            await Assert.That(header).Contains("secure");
            await Assert.That(header).Contains("httponly");
            await Assert.That(header).Contains("samesite=lax");
            await Assert.That(header).DoesNotContain("domain");
        }
    }

    [Test]
    public async Task A_session_is_touched_at_most_once_a_minute()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        host.Time.Advance(TimeSpan.FromSeconds(30));
        using var within = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));
        var commitsWithin = host.UnitOfWork.Commits;
        host.Time.Advance(TimeSpan.FromSeconds(30));
        using var due = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));
        var commitsDue = host.UnitOfWork.Commits;
        using var right = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        using var body = JsonDocument.Parse(await due.Content.ReadAsStringAsync());
        var touchedAt = IdentitySessionHost.Start + Duration.FromMinutes(1);
        using (Assert.Multiple())
        {
            await Assert.That(within.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(commitsWithin).IsEqualTo(0);
            await Assert.That(commitsDue).IsEqualTo(1);
            await Assert.That(host.UnitOfWork.Commits).IsEqualTo(1);
            await Assert.That(session.LastSeenAt).IsEqualTo(touchedAt);
            await Assert.That(body.RootElement.GetProperty("idleExpiresAt").GetDateTimeOffset())
                .IsEqualTo((touchedAt + Duration.FromHours(12)).ToDateTimeOffset());
        }
    }

    [Test]
    public async Task A_touch_that_loses_the_race_is_logged_and_counted_and_never_fails_the_request()
    {
        // Arrange
        using var logs = new CapturedLogs();
        await using var host = await IdentitySessionHost.StartAsync(logs);
        var (_, _, token) = host.Seed();
        host.UnitOfWork.LoseTheRace = true;
        using var metrics = new MetricCapture(host.App.Services.GetRequiredService<IMeterFactory>());
        using var client = host.App.GetTestClient();
        host.Time.Advance(TimeSpan.FromMinutes(2));

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, Current, bearer: token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(metrics.Of(SessionsMetrics.SessionTouchLostMetric).Count).IsEqualTo(1);
            await Assert.That(logs.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("lost the race"))).IsEqualTo(1);
            await Assert.That(host.UnitOfWork.Rollbacks).IsEqualTo(1);
        }
    }

    [Test]
    public async Task The_session_routes_are_in_the_contract_with_their_challenge_and_origin_answers()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        using var client = host.App.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");

        // Assert
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());
        var path = document.RootElement.GetProperty("paths").GetProperty(Current);
        using (Assert.Multiple())
        {
            await Assert.That(path.GetProperty("get").GetProperty("operationId").GetString()).IsEqualTo("GetCurrentSession");
            await Assert.That(path.GetProperty("delete").GetProperty("operationId").GetString()).IsEqualTo("LogOut");
            await Assert.That(path.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _)).IsTrue();
            await Assert.That(path.GetProperty("get").GetProperty("responses").TryGetProperty("401", out _)).IsTrue();
        }
    }
}
