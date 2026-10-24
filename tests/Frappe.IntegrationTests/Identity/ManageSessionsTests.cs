using System.Text.Json;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary><c>GET /v1/identity/sessions</c> and <c>DELETE /v1/identity/sessions/{id}</c>.</summary>
public sealed class ManageSessionsTests(ApiFixture fixture)
{
    private const string Sessions = "/v1/identity/sessions";

    [Fact]
    public async Task List_ReturnsTheOwnersLiveSessions_FlaggingTheCurrentOne_WithoutTokens()
    {
        var (principal, current, token) = await SessionSeeder.SeedAsync(fixture.Host);
        var other = await OpenAnotherSessionAsync(principal.Id);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url(Sessions);
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(200);
        });

        var body = await result.ReadAsTextAsync();
        body.ShouldNotContain(token);
        body.ShouldNotContain("token", Case.Insensitive);
        var items = JsonDocument.Parse(body).RootElement.EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        var byId = items.ToDictionary(i => i.GetProperty("id").GetGuid());
        byId[current.Id].GetProperty("current").GetBoolean().ShouldBeTrue();
        byId[other].GetProperty("current").GetBoolean().ShouldBeFalse();
        byId[current.Id].GetProperty("createdAt").GetDateTimeOffset().ShouldBe(current.CreatedAt, TimeSpan.FromMilliseconds(1));
        byId[current.Id].TryGetProperty("lastSeenAt", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task List_OmitsAnotherOwnersSessions()
    {
        await SessionSeeder.SeedAsync(fixture.Host);
        var (_, mine, token) = await SessionSeeder.SeedAsync(fixture.Host);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url(Sessions);
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(200);
        });

        var ids = JsonDocument.Parse(await result.ReadAsTextAsync()).RootElement.EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        ids.ShouldBe([mine.Id]);
    }

    [Fact]
    public async Task List_Anonymous_IsUnauthorized() =>
        await fixture.Host.Scenario(s =>
        {
            s.Get.Url(Sessions);
            s.StatusCodeShouldBe(401);
        });

    [Fact]
    public async Task Revoke_OwnSession_Returns204_AndRemovesIt()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        var other = await OpenAnotherSessionAsync(principal.Id);

        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{other}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(204);
        });

        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Sessions.ShouldNotContain(x => x.Id == other);
    }

    [Fact]
    public async Task Revoke_WithCookie_NeedsTheCsrfHeader()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        var other = await OpenAnotherSessionAsync(principal.Id);
        var cookie = $"{ApiFixture.SessionCookieName}={token}";

        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{other}");
            s.WithRequestHeader("Cookie", cookie);
            s.StatusCodeShouldBe(401);
        });
        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{other}");
            s.WithRequestHeader("Cookie", cookie);
            s.WithRequestHeader("X-Requested-With", "frappe");
            s.StatusCodeShouldBe(204);
        });
    }

    [Fact]
    public async Task Revoke_AnotherOwnersSession_Returns404_AndLeavesItAlive()
    {
        var (victim, victimSession, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{victimSession.Id}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(404);
        });

        (await SessionSeeder.LoadAsync(fixture.Host, victim.Id)).Sessions.ShouldContain(x => x.Id == victimSession.Id);
    }

    [Fact]
    public async Task Revoke_UnknownSession_Returns404()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{Guid.CreateVersion7()}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(404);
        });

        await result.ShouldBeProblemAsync(404, "identity.session.not_found");
    }

    [Fact]
    public async Task Revoke_TheCurrentSession_EndsIt()
    {
        var (_, current, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{current.Id}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(204);
        });
        await fixture.Host.Scenario(s =>
        {
            s.Get.Url(Sessions);
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(401);
        });
    }

    private async Task<Guid> OpenAnotherSessionAsync(Guid principalId)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IPrincipalRepository>();
        var principal = (await repository.GetByIdAsync(principalId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var hash = services.GetRequiredService<ISecretHasher>().Hash(services.GetRequiredService<ISecretGenerator>().NewSessionToken());
        var session = principal.OpenSession(hash, SessionSurface.Console, services.GetRequiredService<TimeProvider>().GetUtcNow()).Value;
        (await repository.SaveAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        return session.Id;
    }

    [Fact]
    public async Task RevokesRacingWritesToTheSameAccount_NeverAnswer500()
    {
        const string password = "correct horse battery";
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host, password);
        var others = new List<Guid>();
        for (var i = 0; i < 4; i++)
            others.Add((await SessionSeeder.OpenSessionAsync(fixture.Host, principal.Id)).Session.Id);

        // Step-ups write the same aggregate, so revokes lose optimistic-concurrency races and must retry or answer 409.
        var revokes = others.Select(id => fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{id}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.IgnoreStatusCode();
        }));
        var stepUps = others.Select(_ => fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { password }).ToUrl($"{Sessions}/current/step-ups");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.IgnoreStatusCode();
        }));
        var results = await Task.WhenAll(revokes.Concat(stepUps));

        results.Select(r => r.Context.Response.StatusCode).ShouldAllBe(code => code == 204 || code == 201 || code == 409);
        var conflicts = results.Where(r => r.Context.Response.StatusCode == 409).ToList();
        foreach (var conflict in conflicts)
            (await conflict.ReadAsTextAsync()).ShouldContain("identity.account.changed");
        var stored = await SessionSeeder.LoadAsync(fixture.Host, principal.Id);
        var revoked = results.Take(others.Count).Count(r => r.Context.Response.StatusCode == 204);
        stored.Sessions.Count.ShouldBe(1 + others.Count - revoked);
    }
}
