using Microsoft.Extensions.Time.Testing;
using System.Text.Json;
using Alba;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain.Principals;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Changing the sign-in email: requested after a step-up, swapped only once the new address proves itself.</summary>
public sealed class EmailChangeTests(EmailChangeTests.HostFixture hostFixture) : IClassFixture<EmailChangeTests.HostFixture>
{
    private const string EmailChanges = "/v1/identity/me/email-changes";
    private const string Password = "correct horse battery";

    private readonly FakeTimeProvider _clock = hostFixture.Clock.Reset();
    private readonly IAlbaHost _host = hostFixture.Host;

    /// <summary>One host for the whole class: booting a host per test cost about a second each.</summary>
    public sealed class HostFixture(ApiFixture fixture) : IAsyncLifetime
    {
        internal FakeTimeProvider Clock { get; } = TestTime.NewClock();

        public IAlbaHost Host { get; private set; } = null!;

        public async ValueTask InitializeAsync() =>
            Host = await fixture.CreateHostAsync(services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<ISecretGenerator>(new FixedCodeGenerator());
            });

        // A failed start leaves no host: disposing must not hide that error behind a null reference.
        public async ValueTask DisposeAsync()
        {
            if (Host is not null)
                await Host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Request_WithoutAStepUp_Answers403AndLeavesNothingPending()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(_host, Password);

        var result = await RequestAsync(token, NewEmail(), 403);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.step_up.required"]);
        (await SessionSeeder.LoadAsync(_host, principal.Id)).Owner.PendingEmail.ShouldBeNull();
    }

    [Fact]
    public async Task Request_Anonymous_Answers401() =>
        await _host.Scenario(s =>
        {
            s.Post.Json(new { newEmail = NewEmail() }).ToUrl(EmailChanges);
            s.StatusCodeShouldBe(401);
        });

    [Fact]
    public async Task RequestThenConfirm_SwapsTheEmail_KeepingTheOldOneUntilConfirmed()
    {
        var (principal, _, token) = await SeedSteppedUpAsync();
        var oldEmail = principal.Owner.Email;
        var newEmail = NewEmail();

        var result = await RequestAsync(token, newEmail, 201);

        var id = await IdAsync(result);
        var pending = await SessionSeeder.LoadAsync(_host, principal.Id);
        pending.Owner.Email.ShouldBe(oldEmail);
        pending.Owner.PendingEmail.ShouldNotBeNull().Value.ShouldBe(newEmail);
        pending.Codes.Single(c => c.Purpose == CodePurpose.EmailChange).Id.ShouldBe(id);
        result.Context.Response.Headers.Location.ToString().ShouldBe($"{EmailChanges}/{id}");

        await ConfirmAsync(token, id, FixedCodeGenerator.Code, 204);

        var confirmed = await SessionSeeder.LoadAsync(_host, principal.Id);
        confirmed.Owner.Email.Value.ShouldBe(newEmail);
        confirmed.Owner.PendingEmail.ShouldBeNull();
    }

    [Fact]
    public async Task Request_PublishesTheCodeForTheNewAddress()
    {
        var (principal, _, token) = await SeedSteppedUpAsync();
        var newEmail = NewEmail();
        IScenarioResult result = null!;

        var tracked = await _host.ExecuteAndWaitAsync(async () => result = await RequestAsync(token, newEmail, 201));

        var issued = tracked.Sent.MessagesOf<EmailChangeCodeIssued>().ShouldHaveSingleItem();
        issued.ChangeId.ShouldBe(await IdAsync(result));
        issued.PrincipalId.ShouldBe(principal.Id);
        issued.Name.ShouldBe(principal.Owner.Name.Value);
        issued.Email.ShouldBe(newEmail);
        issued.Code.ShouldBe(FixedCodeGenerator.Code);
        issued.ExpiresAt.ShouldBeGreaterThan(_clock.GetUtcNow());
    }

    [Fact]
    public async Task Request_Refused_PublishesNoCode()
    {
        var other = (await SessionSeeder.SeedAsync(_host, Password)).Principal;
        var (_, _, token) = await SeedSteppedUpAsync();

        var tracked = await _host.ExecuteAndWaitAsync(async () =>
            await RequestAsync(token, other.Owner.Email.Value, 409));

        tracked.Sent.MessagesOf<EmailChangeCodeIssued>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_ToAnEmailAnotherOwnerUses_Answers409()
    {
        var (other, _, _) = await SessionSeeder.SeedAsync(_host, Password);
        var (principal, _, token) = await SeedSteppedUpAsync();

        var result = await RequestAsync(token, other.Owner.Email.Value.ToUpperInvariant(), 409);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.email.taken"]);
        (await SessionSeeder.LoadAsync(_host, principal.Id)).Owner.PendingEmail.ShouldBeNull();
    }

    [Fact]
    public async Task Request_WithAMalformedEmail_Answers422OnNewEmail()
    {
        var (_, _, token) = await SeedSteppedUpAsync();

        var result = await RequestAsync(token, "not-an-email", 422);

        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        var error = body.RootElement.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("code").GetString().ShouldBe("identity.email.invalid");
        error.GetProperty("field").GetString().ShouldBe("newEmail");
    }

    [Fact]
    public async Task Request_BeyondTheHourlyLimit_Answers429WithRetryAfter()
    {
        var (_, _, token) = await SeedSteppedUpAsync();
        for (var i = 0; i < AttemptPolicies.EmailChangeRequestsPerPrincipal.Limit; i++)
            await RequestAsync(token, NewEmail(), 201);

        var result = await RequestAsync(token, NewEmail(), 429);

        result.Context.Response.Headers.RetryAfter.ToString().ShouldNotBeNullOrEmpty();
        (await ErrorCodesAsync(result)).ShouldBe(["identity.attempts.too_many"]);
    }

    [Fact]
    public async Task Confirm_WithAWrongCode_Answers422AndKeepsTheOldEmail()
    {
        var (principal, _, token) = await SeedSteppedUpAsync();
        var id = await IdAsync(await RequestAsync(token, NewEmail(), 201));

        var result = await ConfirmAsync(token, id, "000000", 422);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.code.invalid"]);
        (await SessionSeeder.LoadAsync(_host, principal.Id)).Owner.Email.ShouldBe(principal.Owner.Email);
    }

    [Fact]
    public async Task Confirm_AfterTheCodeExpired_Answers422Expired()
    {
        var (_, _, token) = await SeedSteppedUpAsync();
        var id = await IdAsync(await RequestAsync(token, NewEmail(), 201));
        _clock.Advance(VerificationCode.Lifetime);

        var result = await ConfirmAsync(token, id, FixedCodeGenerator.Code, 422);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.code.expired"]);
    }

    [Fact]
    public async Task Confirm_AnUnknownChange_Answers404()
    {
        var (_, _, token) = await SeedSteppedUpAsync();
        await RequestAsync(token, NewEmail(), 201);

        var result = await ConfirmAsync(token, Guid.NewGuid(), FixedCodeGenerator.Code, 404);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.email_change.not_found"]);
    }

    [Fact]
    public async Task Confirm_AfterTooManyWrongCodes_Answers429_EvenForTheRightOne()
    {
        var (principal, _, token) = await SeedSteppedUpAsync();
        var id = await IdAsync(await RequestAsync(token, NewEmail(), 201));
        for (var i = 0; i < AttemptPolicies.EmailChangeConfirmFailuresPerPrincipal.Limit; i++)
            await ConfirmAsync(token, id, "000000", 422);

        var result = await ConfirmAsync(token, id, FixedCodeGenerator.Code, 429);

        result.Context.Response.Headers.RetryAfter.ToString().ShouldNotBeNullOrEmpty();
        (await SessionSeeder.LoadAsync(_host, principal.Id)).Owner.PendingEmail.ShouldNotBeNull();
    }

    [Fact]
    public async Task Confirm_ByCookie_WithTheCsrfHeader_Answers204()
    {
        var (_, _, token) = await SeedSteppedUpAsync();
        var id = await IdAsync(await RequestAsync(token, NewEmail(), 201));

        await _host.Scenario(s =>
        {
            s.Post.Json(new { code = FixedCodeGenerator.Code }).ToUrl($"{EmailChanges}/{id}/confirmation");
            s.WithRequestHeader("Cookie", $"{ApiFixture.SessionCookieName}={token}");
            s.WithRequestHeader("X-Requested-With", "frappe");
            s.StatusCodeShouldBe(204);
        });
    }

    private async Task<(Principal Principal, Session Session, string Token)> SeedSteppedUpAsync()
    {
        var seeded = await SessionSeeder.SeedAsync(_host, Password);
        await _host.Scenario(s =>
        {
            s.Post.Json(new { password = Password }).ToUrl("/v1/identity/sessions/current/step-ups");
            Bearer(s, seeded.Token);
            s.StatusCodeShouldBe(201);
        });
        return seeded;
    }

    private Task<IScenarioResult> RequestAsync(string token, string newEmail, int status) =>
        _host.Scenario(s =>
        {
            s.Post.Json(new { newEmail }).ToUrl(EmailChanges);
            Bearer(s, token);
            s.StatusCodeShouldBe(status);
        });

    private Task<IScenarioResult> ConfirmAsync(string token, Guid id, string code, int status) =>
        _host.Scenario(s =>
        {
            s.Post.Json(new { code }).ToUrl($"{EmailChanges}/{id}/confirmation");
            Bearer(s, token);
            s.StatusCodeShouldBe(status);
        });

    private static string NewEmail() => $"new-{Guid.NewGuid():N}@example.com";

    private static void Bearer(Scenario s, string token) => s.WithRequestHeader("Authorization", $"Bearer {token}");

    private static async Task<Guid> IdAsync(IScenarioResult result)
    {
        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string?[]> ErrorCodesAsync(IScenarioResult result)
    {
        using var body = JsonDocument.Parse(await result.ReadAsTextAsync());
        return [.. body.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString())];
    }
}
