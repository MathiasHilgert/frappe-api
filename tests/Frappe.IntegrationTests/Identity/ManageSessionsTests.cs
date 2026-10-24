using System.Text.Json;
using Alba;
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
        byId[current.Id].GetProperty("created_at").GetDateTimeOffset().ShouldBe(current.CreatedAt, TimeSpan.FromMilliseconds(1));
        byId[current.Id].TryGetProperty("last_seen_at", out _).ShouldBeTrue();
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

        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/{Guid.CreateVersion7()}");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(404);
        });
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
        var session = principal.OpenSession(hash, SessionSurface.Console, services.GetRequiredService<TimeProvider>()).Value;
        (await repository.SaveChangesAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        return session.Id;
    }
}
