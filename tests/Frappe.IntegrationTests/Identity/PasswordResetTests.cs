using Alba;
using Frappe.Identity.Application.PasswordResets;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts.PasswordResets;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Reset by a six-digit code: no account enumeration, rate limited, and every session revoked on success.</summary>
public sealed class PasswordResetTests(ApiFixture fixture)
{
    private const string Requests = "/v1/identity/password-resets";
    private const string NewPassword = "a-brand-new-long-passphrase";

    [Fact]
    public async Task Request_ForAnOwner_Answers202WithTheResetId_AndIssuesACode()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);

        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);

        issued.ShouldNotBeNull();
        issued.ResetId.ShouldBe(id);
        issued.PrincipalId.ShouldBe(principal.Id);
        issued.Email.ShouldBe(principal.Owner.Email.Value);
        CodeOf(issued).ShouldMatch("^[0-9]{6}$");
        issued.SealedCode.ShouldNotContain(CodeOf(issued));
        var stored = (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Codes.Single(c => c.Purpose == CodePurpose.PasswordReset);
        stored.Id.ShouldBe(id);
        string.Equals(stored.CodeHash, CodeOf(issued), StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Request_ForAnUnknownEmail_Answers202WithAnId_AndSendsNothing()
    {
        await MigrateAsync();
        var (id, issued) = await RequestAsync($"nobody-{Guid.NewGuid():N}@example.com");

        id.ShouldNotBe(Guid.Empty);
        issued.ShouldBeNull();
    }

    [Fact]
    public async Task Request_KnownOrUnknown_DoesTheSameWorkBeforeAnswering_AndIssuesInTheBackground()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";

        var known = await RequestTrackedAsync(principal.Owner.Email.Value);
        var missing = await RequestTrackedAsync(unknown);

        // The request handler only queues the issue command; the account lookup happens after the answer.
        known.Queued.ShouldBe(new IssuePasswordResetCode(known.Id, principal.Owner.Email.Value));
        missing.Queued.ShouldBe(new IssuePasswordResetCode(missing.Id, unknown));
        known.Issued.ShouldNotBeNull().ResetId.ShouldBe(known.Id);
        missing.Issued.ShouldBeNull();
    }

    [Fact]
    public async Task Confirm_WithAWrongCodeAndAWeakPassword_ReportsTheCode_BeforeCheckingThePassword()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);
        var wrong = CodeOf(issued) == "000000" ? "111111" : "000000";

        var body = await ConfirmAsync(id, wrong, "short", 422);

        body.ShouldContain("identity.code.invalid");
        body.ShouldNotContain("identity.password.too_short");
    }

    [Fact]
    public async Task Request_WithAMalformedEmail_Is422()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { email = "not-an-email" }).ToUrl(Requests);
            s.StatusCodeShouldBe(422);
        });

        (await result.ReadAsTextAsync()).ShouldContain("identity.email.invalid");
    }

    [Fact]
    public async Task Request_IsRateLimitedPerEmail_WhetherOrNotTheAccountExists()
    {
        await MigrateAsync();
        var email = $"nobody-{Guid.NewGuid():N}@example.com";
        for (var i = 0; i < AttemptPolicies.PasswordResetPerEmail.Limit; i++)
            await RequestAsync(email);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { email }).ToUrl(Requests);
            s.StatusCodeShouldBe(429);
        });
        result.Context.Response.Headers.RetryAfter.ToString().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Confirm_WithTheCode_ChangesThePassword_AndRevokesEverySession()
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await OpenAnotherSessionAsync(principal.Id);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);

        await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { code = CodeOf(issued), newPassword = NewPassword }).ToUrl($"{Requests}/{id}/confirmation");
            s.StatusCodeShouldBe(204);
        });

        var reloaded = await SessionSeeder.LoadAsync(fixture.Host, principal.Id);
        reloaded.Sessions.ShouldBeEmpty();
        reloaded.Codes.Single(c => c.Purpose == CodePurpose.PasswordReset).UsedAt.ShouldNotBeNull();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IPasswordHasher>()
            .VerifyAsync(NewPassword, reloaded.Owner.PasswordHash, CancellationToken.None)).ShouldBeTrue();
        await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/v1/does-not-exist");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(401);
        });
    }

    [Fact]
    public async Task Confirm_TheSameCodeTwice_IsRejected()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);
        await ConfirmAsync(id, CodeOf(issued), NewPassword, 204);

        var body = await ConfirmAsync(id, CodeOf(issued), NewPassword, 422);

        body.ShouldContain("identity.code.already_used");
    }

    [Fact]
    public async Task Confirm_WithAWrongCode_Is422_AndKeepsThePassword()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);

        var body = await ConfirmAsync(id, CodeOf(issued) == "000000" ? "111111" : "000000", NewPassword, 422);

        body.ShouldContain("identity.code.invalid");
        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Owner.PasswordHash.ShouldBe("password-hash");
    }

    [Fact]
    public async Task Confirm_AnUnknownReset_Is422CodeInvalid()
    {
        await MigrateAsync();
        (await ConfirmAsync(Guid.CreateVersion7(), "123456", NewPassword, 422)).ShouldContain("identity.code.invalid");
    }

    [Fact]
    public async Task Confirm_WithAShortPassword_Is422_AndLeavesTheCodeUsable()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);

        (await ConfirmAsync(id, CodeOf(issued), "short", 422)).ShouldContain("identity.password.too_short");

        await ConfirmAsync(id, CodeOf(issued), NewPassword, 204);
    }

    [Fact]
    public async Task Confirm_AttemptsAreRateLimitedPerReset_EvenForTheRightCode()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);
        var wrong = CodeOf(issued) == "000000" ? "111111" : "000000";
        for (var i = 0; i < AttemptPolicies.PasswordResetConfirmFailuresPerReset.Limit; i++)
            await ConfirmAsync(id, wrong, NewPassword, 422);

        await ConfirmAsync(id, CodeOf(issued), NewPassword, 429);

        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Owner.PasswordHash.ShouldBe("password-hash");
    }

    [Fact]
    public async Task Confirm_AParallelBurstOfWrongCodes_GetsOnlyTheLimitOfTries()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);
        var (id, issued) = await RequestAsync(principal.Owner.Email.Value);
        var wrong = CodeOf(issued) == "000000" ? "111111" : "000000";

        var statuses = await Burst.StatusesAsync(fixture.Host,
            s => s.Post.Json(new { code = wrong, newPassword = NewPassword }).ToUrl($"{Requests}/{id}/confirmation"));

        var limit = AttemptPolicies.PasswordResetConfirmFailuresPerReset.Limit;
        statuses.Count(s => s == 422).ShouldBe(limit);
        statuses.Count(s => s == 429).ShouldBe(Burst.Size - limit);
        await ConfirmAsync(id, CodeOf(issued), NewPassword, 429);
        (await SessionSeeder.LoadAsync(fixture.Host, principal.Id)).Owner.PasswordHash.ShouldBe("password-hash");
    }

    private async Task<(Guid Id, PasswordResetCodeIssued? Issued)> RequestAsync(string email)
    {
        var (id, issued, _) = await RequestTrackedAsync(email);
        return (id, issued);
    }

    private async Task<(Guid Id, PasswordResetCodeIssued? Issued, IssuePasswordResetCode? Queued)> RequestTrackedAsync(string email)
    {
        IScenarioResult? result = null;
        var tracked = await fixture.Host
            .TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync((Func<Wolverine.IMessageContext, Task>)(async _ =>
                result = await fixture.Host.Scenario(s =>
                {
                    s.Post.Json(new { email }).ToUrl(Requests);
                    s.StatusCodeShouldBe(202);
                })));

        var body = await result!.ReadAsJsonAsync<ResetAccepted>();
        var messages = tracked.AllRecordsInOrder().Select(r => r.Message).ToList();
        return (body.Id, messages.OfType<PasswordResetCodeIssued>().FirstOrDefault(),
            messages.OfType<IssuePasswordResetCode>().FirstOrDefault());
    }

    private string CodeOf(PasswordResetCodeIssued? issued) => CodeSeal.Open(fixture.Host, issued.ShouldNotBeNull().SealedCode);

    private async Task<string> ConfirmAsync(Guid id, string code, string newPassword, int status)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { code, newPassword }).ToUrl($"{Requests}/{id}/confirmation");
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsTextAsync();
    }

    private async Task OpenAnotherSessionAsync(Guid principalId)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPrincipalRepository>();
        var principal = (await repository.GetByIdAsync(principalId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        principal.OpenSession($"other-{Guid.NewGuid():N}", SessionSurface.Console, TimeProvider.System.GetUtcNow()).IsSuccess.ShouldBeTrue();
        (await repository.SaveAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }

    private async Task MigrateAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    private sealed record ResetAccepted(Guid Id);
}
