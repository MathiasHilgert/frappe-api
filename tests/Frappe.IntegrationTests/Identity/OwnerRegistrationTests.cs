using System.Net;
using System.Text.Json;
using Alba;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests.Identity;

/// <summary>POST /v1/identity/owners and POST /v1/identity/owners/{id}/verification-codes.</summary>
public sealed class OwnerRegistrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Owners = "/v1/identity/owners";
    private const string ValidPassword = "a long enough passphrase";

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Register_Valid_CreatesPendingOwnerWithHashedCode_AndPublishesTheCode()
    {
        var email = UniqueEmail();
        IScenarioResult result = null!;

        var tracked = await fixture.Host.ExecuteAndWaitAsync(async () =>
            result = await PostRegister(new { name = "Ana", email, password = ValidPassword }, UniqueIp(), 201));

        var id = (await result.ReadAsJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var principal = await SessionSeeder.LoadAsync(fixture.Host, id);
        principal.Owner.Status.ShouldBe(OwnerStatus.Pending);
        principal.Owner.Name.Value.ShouldBe("Ana");
        principal.Owner.Email.Value.ShouldBe(email);
        principal.Owner.PasswordHash.ShouldNotContain(ValidPassword);

        var issued = tracked.Sent.MessagesOf<OwnerVerificationCodeIssued>().ShouldHaveSingleItem();
        issued.PrincipalId.ShouldBe(id);
        issued.Email.ShouldBe(email);
        var code = principal.Codes.ShouldHaveSingleItem();
        code.Purpose.ShouldBe(CodePurpose.EmailVerify);
        code.CodeHash.ShouldBe(Hash(issued.Code));
        tracked.AllRecordsInOrder().Select(r => r.Message).OfType<OwnerRegistrationSubmitted>()
            .ShouldContain(e => e.PrincipalId == id);
    }

    [Fact]
    public async Task Register_TakenEmail_InAnyCase_Is409EmailTaken()
    {
        var email = UniqueEmail();
        await PostRegister(new { name = "Ana", email, password = ValidPassword }, UniqueIp(), 201);

        var result = await PostRegister(new { name = "Bea", email = email.ToUpperInvariant(), password = ValidPassword }, UniqueIp(), 409);

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("email_taken");
    }

    [Fact]
    public async Task Register_InvalidInput_Is422WithEveryBrokenRule()
    {
        var result = await PostRegister(new { name = "", email = "not-an-email", password = "short" }, UniqueIp(), 422);

        var codes = (await result.ReadAsJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("code").GetString()).ToList();
        codes.ShouldBe(["identity.name.required", "identity.email.invalid", "identity.password.too_short"], ignoreOrder: true);
    }

    [Fact]
    public async Task Register_TooLongPassword_Is422()
    {
        var result = await PostRegister(new { name = "Ana", email = UniqueEmail(), password = new string('x', 129) }, UniqueIp(), 422);

        (await result.ReadAsTextAsync()).ShouldContain("identity.password.too_long");
    }

    [Fact]
    public async Task Register_MalformedJson_Is400() =>
        await fixture.Host.Scenario(s =>
        {
            s.Post.Text("{").ToUrl(Owners);
            s.WithRequestHeader("Content-Type", "application/json");
            s.StatusCodeShouldBe(400);
        });

    [Fact]
    public async Task Register_SameEmailConcurrently_OneWinsTheOtherIs409()
    {
        var email = UniqueEmail();
        var attempts = Enumerable.Range(0, 2).Select(_ => fixture.Host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = UniqueIp());
            s.Post.Json(new { name = "Ana", email, password = ValidPassword }).ToUrl(Owners);
            s.IgnoreStatusCode();
        }));

        var statuses = (await Task.WhenAll(attempts)).Select(r => r.Context.Response.StatusCode).Order().ToList();

        statuses.ShouldBe([201, 409]);
    }

    [Fact]
    public async Task Register_SixthAttemptForOneEmail_Is429WithRetryAfter()
    {
        var email = UniqueEmail();
        for (var i = 0; i < AttemptPolicies.RegisterPerEmail.Limit; i++)
            await PostRegister(new { name = "Ana", email, password = "short" }, UniqueIp(), 422);

        var result = await PostRegister(new { name = "Ana", email, password = ValidPassword }, UniqueIp(), 429);

        RetryAfterSeconds(result).ShouldBeInRange(1, 3600);
        // The same problem shape as every other throttled command answered through ToProblem.
        var body = await result.ReadAsTextAsync();
        body.ShouldContain("\"type\":\"urn:frappe:problem:identity.attempts.too_many\"");
        body.ShouldContain("\"code\":\"identity.attempts.too_many\"");
    }

    [Fact]
    public async Task Register_BeyondThePerIpLimit_Is429()
    {
        var ip = UniqueIp();
        for (var i = 0; i < AttemptPolicies.RegisterPerIp.Limit; i++)
            await PostRegister(new { name = "Ana", email = UniqueEmail(), password = "short" }, ip, 422);

        var result = await PostRegister(new { name = "Ana", email = UniqueEmail(), password = ValidPassword }, ip, 429);

        RetryAfterSeconds(result).ShouldBeInRange(1, 3600);
    }

    [Fact]
    public async Task Resend_PendingOwner_Is202_ReplacesTheCode_AndPublishesIt()
    {
        var id = await RegisterAsync();
        var before = (await SessionSeeder.LoadAsync(fixture.Host, id)).Codes.Single().CodeHash;

        var tracked = await fixture.Host.ExecuteAndWaitAsync(() => PostResend(id, 202));

        var issued = tracked.Sent.MessagesOf<OwnerVerificationCodeIssued>().ShouldHaveSingleItem();
        var code = (await SessionSeeder.LoadAsync(fixture.Host, id)).Codes.ShouldHaveSingleItem();
        code.CodeHash.ShouldBe(Hash(issued.Code));
        code.CodeHash.ShouldNotBe(before, StringComparer.Ordinal);
        code.ResendCount.ShouldBe(1);
    }

    [Fact]
    public async Task Resend_SixthWithinAnHour_Is429WithRetryAfter()
    {
        var id = await RegisterAsync();
        for (var i = 0; i < VerificationCode.MaxResendsPerHour; i++)
            await PostResend(id, 202);

        var result = await PostResend(id, 429);

        RetryAfterSeconds(result).ShouldBeInRange(3000, 3600);
        (await result.ReadAsTextAsync()).ShouldContain("identity.code.resend_limit_reached");
    }

    [Fact]
    public async Task Resend_UnknownOwner_Is404() => await PostResend(Guid.CreateVersion7(), 404);

    [Fact]
    public async Task Resend_VerifiedOwner_Is409()
    {
        var (principal, _, _) = await SessionSeeder.SeedAsync(fixture.Host);

        await PostResend(principal.Id, 409);
    }

    private async Task<Guid> RegisterAsync()
    {
        var result = await PostRegister(new { name = "Ana", email = UniqueEmail(), password = ValidPassword }, UniqueIp(), 201);
        return (await result.ReadAsJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<IScenarioResult> PostRegister(object body, IPAddress ip, int status) =>
        fixture.Host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = ip);
            s.Post.Json(body).ToUrl(Owners);
            s.StatusCodeShouldBe(status);
        });

    private Task<IScenarioResult> PostResend(Guid id, int status) =>
        fixture.Host.Scenario(s =>
        {
            s.Post.Url($"{Owners}/{id}/verification-codes");
            s.StatusCodeShouldBe(status);
        });

    private string Hash(string code) => fixture.Host.Services.GetRequiredService<ISecretHasher>().Hash(code);

    private static int RetryAfterSeconds(IScenarioResult result) =>
        int.Parse(result.Context.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture);

    private static string UniqueEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    private static IPAddress UniqueIp() => new(Guid.NewGuid().ToByteArray());
}
