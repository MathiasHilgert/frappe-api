using System.Text.Json;
using Alba;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Step-up and change password: re-proving the password inside a signed-in session.</summary>
public sealed class AccountSecurityTests(ApiFixture fixture, AccountSecurityTests.ClockHostFixture clockHost)
    : IClassFixture<AccountSecurityTests.ClockHostFixture>
{
    private const string StepUps = "/v1/identity/sessions/current/step-ups";
    private const string MyPassword = "/v1/identity/me/password";
    private const string Password = "correct horse battery";
    private const string NewPassword = "a brand new passphrase";

    private readonly FakeTimeProvider _clock = clockHost.Clock.Reset();

    /// <summary>One host on a test clock for the whole class: booting a host per test cost about a second each.</summary>
    public sealed class ClockHostFixture(ApiFixture fixture) : IAsyncLifetime
    {
        internal FakeTimeProvider Clock { get; } = TestTime.NewClock();

        public IAlbaHost Host { get; private set; } = null!;

        public async ValueTask InitializeAsync() =>
            Host = await fixture.CreateHostAsync(services => services.AddSingleton<TimeProvider>(Clock));

        // A failed start leaves no host: disposing must not hide that error behind a null reference.
        public async ValueTask DisposeAsync()
        {
            if (Host is not null)
                await Host.DisposeAsync();
        }
    }

    [Fact]
    public async Task StepUp_WithTheRightPassword_Answers201AndMarksTheSessionForFiveMinutes()
    {
        var (host, clock) = (clockHost.Host, _clock);
        var (principal, session, token) = await SessionSeeder.SeedAsync(host, Password);

        var result = await host.Scenario(s =>
        {
            s.Post.Json(new { password = Password }).ToUrl(StepUps);
            Bearer(s, token);
            s.StatusCodeShouldBe(201);
        });

        var expected = clock.GetUtcNow() + Session.StepUpValidity;
        (await result.ReadAsJsonAsync<StepUpResponse>()).ShouldNotBeNull().ExpiresAt.ShouldBe(expected, TimeSpan.FromMilliseconds(1));
        var stored = (await SessionSeeder.LoadAsync(host, principal.Id)).Sessions.Single(x => x.Id == session.Id);
        stored.StepUpExpiresAt.ShouldNotBeNull().ShouldBe(expected, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task StepUp_ByCookie_WithTheCsrfHeader_Answers201()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);

        await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { password = Password }).ToUrl(StepUps);
            s.WithRequestHeader("Cookie", $"{ApiFixture.SessionCookieName}={token}");
            s.WithRequestHeader("X-Requested-With", "frappe");
            s.StatusCodeShouldBe(201);
        });
    }

    [Fact]
    public async Task StepUp_WithAWrongPassword_Answers422AndLeavesTheSessionUnmarked()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { password = "not my password" }).ToUrl(StepUps);
            Bearer(s, token);
            s.StatusCodeShouldBe(422);
        });

        (await ErrorCodesAsync(result)).ShouldBe(["identity.step_up.password_incorrect"]);
        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Sessions.Single().StepUpExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task StepUp_Anonymous_Answers401() =>
        await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { password = Password }).ToUrl(StepUps);
            s.StatusCodeShouldBe(401);
        });

    [Fact]
    public async Task StepUp_AfterFiveWrongPasswords_Answers429WithRetryAfter_EvenForTheRightOne()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);
        for (var i = 0; i < AttemptPolicies.ReverifyFailuresPerPrincipal.Limit; i++)
            await StepUpAsync(token, "not my password", 422);

        var result = await StepUpAsync(token, Password, 429);

        int.Parse(result.Context.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeInRange(1, (int)AttemptPolicies.ReverifyFailuresPerPrincipal.Window.TotalSeconds);
        (await ErrorCodesAsync(result)).ShouldBe(["identity.attempts.too_many"]);
        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Sessions.Single().StepUpExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task ChangePassword_WithTheCurrentOne_Answers204_SwapsTheHash_AndRevokesOnlyTheOtherSessions()
    {
        var (principal, current, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);
        var (_, otherToken) = await SessionSeeder.OpenSessionAsync(fixture.Host, principal.Id);

        await ChangePasswordAsync(token, Password, NewPassword, 204);

        var stored = await SessionSeeder.LoadAsync(fixture.Host, principal.Id);
        stored.Sessions.Select(x => x.Id).ShouldBe([current.Id]);
        var hasher = fixture.Host.Services.GetRequiredService<IPasswordHasher>();
        (await hasher.VerifyAsync(NewPassword, stored.Owner.PasswordHash, CancellationToken.None)).ShouldBeTrue();
        (await hasher.VerifyAsync(Password, stored.Owner.PasswordHash, CancellationToken.None)).ShouldBeFalse();
        await StepUpAsync(otherToken, NewPassword, 401);
        await StepUpAsync(token, NewPassword, 201);
    }

    [Fact]
    public async Task ChangePassword_WithAWrongCurrentPassword_Answers422AndChangesNothing()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);
        await SessionSeeder.OpenSessionAsync(fixture.Host, principal.Id);

        var result = await ChangePasswordAsync(token, "not my password", NewPassword, 422);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.password.current_incorrect"]);
        var stored = await SessionSeeder.LoadAsync(fixture.Host, principal.Id);
        stored.Sessions.Count.ShouldBe(2);
        (await fixture.Host.Services.GetRequiredService<IPasswordHasher>()
            .VerifyAsync(Password, stored.Owner.PasswordHash, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePassword_ToAShortPassword_Answers422OnNewPassword()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);
        await SessionSeeder.OpenSessionAsync(fixture.Host, principal.Id);

        var result = await ChangePasswordAsync(token, Password, "short", 422);

        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        var error = body.RootElement.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("code").GetString().ShouldBe("identity.password.too_short");
        error.GetProperty("field").GetString().ShouldBe("newPassword");
        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Sessions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ChangePassword_SharesTheWrongPasswordBudgetWithStepUp()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host, Password);
        for (var i = 0; i < AttemptPolicies.ReverifyFailuresPerPrincipal.Limit; i++)
            await StepUpAsync(token, "not my password", 422);

        var result = await ChangePasswordAsync(token, Password, NewPassword, 429);

        result.Context.Response.Headers.RetryAfter.ToString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task RequireStepUp_RejectsAStaleSessionWith403_AndLetsAFreshOneThrough()
    {
        var (host, clock) = (clockHost.Host, _clock);
        var (_, _, token) = await SessionSeeder.SeedAsync(host, Password);

        // A step-up-guarded route; the malformed email makes getting past the guard answer 422 with no side effect.
        var stale = await GuardedAsync(host, token, 403);
        (await ErrorCodesAsync(stale)).ShouldBe(["identity.step_up.required"]);
        await StepUpAsync(host, token, Password, 201);
        await GuardedAsync(host, token, 422);
        clock.Advance(Session.StepUpValidity);
        await GuardedAsync(host, token, 403);
    }

    private static Task<IScenarioResult> GuardedAsync(IAlbaHost host, string token, int status) =>
        host.Scenario(s =>
        {
            s.Post.Json(new { newEmail = "not-an-email" }).ToUrl("/v1/identity/me/email-changes");
            Bearer(s, token);
            s.StatusCodeShouldBe(status);
        });

    private Task<IScenarioResult> StepUpAsync(string token, string password, int status) =>
        StepUpAsync(fixture.Host, token, password, status);

    private static Task<IScenarioResult> StepUpAsync(IAlbaHost host, string token, string password, int status) =>
        host.Scenario(s =>
        {
            s.Post.Json(new { password }).ToUrl(StepUps);
            Bearer(s, token);
            s.StatusCodeShouldBe(status);
        });

    private Task<IScenarioResult> ChangePasswordAsync(string token, string current, string next, int status) =>
        fixture.Host.Scenario(s =>
        {
            s.Put.Json(new { currentPassword = current, newPassword = next }).ToUrl(MyPassword);
            Bearer(s, token);
            s.StatusCodeShouldBe(status);
        });

    private static void Bearer(Scenario s, string token) => s.WithRequestHeader("Authorization", $"Bearer {token}");

    private static async Task<string?[]> ErrorCodesAsync(IScenarioResult result)
    {
        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        return [.. body.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString())];
    }
}
