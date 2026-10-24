using System.Text.Json;
using Alba;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary><c>POST /v1/identity/owners/{id}/verification</c>: the emailed code activates the owner and signs them in.</summary>
public sealed class OwnerVerificationTests(ApiFixture fixture)
{
    private const string Code = "123456";

    [Fact]
    public async Task RightCode_ActivatesTheOwner_AndOpensASessionForCookieAndBearer()
    {
        var ownerId = await SeedPendingOwnerAsync(fixture.Host);

        var result = await VerifyAsync(fixture.Host, ownerId, Code, 201);

        var body = await result.ReadAsJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString().ShouldNotBeNull();
        var sessionId = body.GetProperty("id").GetGuid();
        body.GetProperty("principalId").GetGuid().ShouldBe(ownerId);
        result.Context.Response.Headers.Location.ToString().ShouldBe($"/v1/identity/sessions/{sessionId}");

        var setCookie = result.Context.Response.Headers.SetCookie.ToString();
        setCookie.ShouldStartWith($"{ApiFixture.SessionCookieName}={token};");
        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldContain("secure", Case.Insensitive);

        var principal = await SessionSeeder.LoadAsync(fixture.Host, ownerId);
        principal.Owner.Status.ShouldBe(OwnerStatus.Active);
        var session = principal.Sessions.ShouldHaveSingleItem();
        session.Id.ShouldBe(sessionId);
        body.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(session.AbsoluteExpiresAt, TimeSpan.FromMilliseconds(1));
        session.TokenHash.ShouldBe(HashOf(fixture.Host, token));
        principal.Codes.Single(c => c.Purpose == CodePurpose.EmailVerify).UsedAt.ShouldNotBeNull();

        // The returned token is a working Bearer credential: an unknown route answers 404, not 401.
        await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/v1/does-not-exist");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task WrongCode_Is422_AndTheOwnerStaysPending()
    {
        var ownerId = await SeedPendingOwnerAsync(fixture.Host);
        using var rejected = new MeterCounter("Frappe.Identity", "frappe.identity.code.invalid");

        var result = await VerifyAsync(fixture.Host, ownerId, "000000", 422);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.code.invalid"]);
        rejected.Total.ShouldBeGreaterThanOrEqualTo(1);
        var principal = await SessionSeeder.LoadAsync(fixture.Host, ownerId);
        principal.Owner.Status.ShouldBe(OwnerStatus.Pending);
        principal.Sessions.ShouldBeEmpty();
        result.Context.Response.Headers.SetCookie.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpiredCode_Is422()
    {
        var clock = TestTime.NewClock();
        await using var host = await fixture.CreateHostAsync(services => services.AddSingleton<TimeProvider>(clock));
        var ownerId = await SeedPendingOwnerAsync(host);

        clock.Advance(VerificationCode.Lifetime);

        var result = await VerifyAsync(host, ownerId, Code, 422);
        (await ErrorCodesAsync(result)).ShouldBe(["identity.code.expired"]);
        (await SessionSeeder.LoadAsync(host, ownerId)).Owner.Status.ShouldBe(OwnerStatus.Pending);
    }

    [Fact]
    public async Task AlreadyVerifiedOwner_Is409_AndOpensNoSecondSession()
    {
        var ownerId = await SeedPendingOwnerAsync(fixture.Host);
        await VerifyAsync(fixture.Host, ownerId, Code, 201);

        var result = await VerifyAsync(fixture.Host, ownerId, Code, 409);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.account.not_pending"]);
        (await SessionSeeder.LoadAsync(fixture.Host, ownerId)).Sessions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UnknownOwner_Is404() =>
        await VerifyAsync(fixture.Host, Guid.CreateVersion7(), Code, 404);

    [Fact]
    public async Task TooManyWrongCodes_Is429_EvenForTheRightCode()
    {
        var ownerId = await SeedPendingOwnerAsync(fixture.Host);
        for (var i = 0; i < AttemptPolicies.VerifyEmailPerOwner.Limit; i++)
            await VerifyAsync(fixture.Host, ownerId, "000000", 422);

        var result = await VerifyAsync(fixture.Host, ownerId, Code, 429);

        (await ErrorCodesAsync(result)).ShouldBe(["identity.attempts.too_many"]);
        int.Parse(result.Context.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThan(0);
        (await SessionSeeder.LoadAsync(fixture.Host, ownerId)).Owner.Status.ShouldBe(OwnerStatus.Pending);
    }

    [Fact]
    public async Task AParallelBurstOfWrongCodes_GetsOnlyTheLimitOfTries()
    {
        var ownerId = await SeedPendingOwnerAsync(fixture.Host);

        var statuses = await Burst.StatusesAsync(fixture.Host,
            s => s.Post.Json(new { code = "000000" }).ToUrl($"/v1/identity/owners/{ownerId}/verification"));

        statuses.Count(s => s == 422).ShouldBe(AttemptPolicies.VerifyEmailPerOwner.Limit);
        statuses.Count(s => s == 429).ShouldBe(Burst.Size - AttemptPolicies.VerifyEmailPerOwner.Limit);
        await VerifyAsync(fixture.Host, ownerId, Code, 429);
        (await SessionSeeder.LoadAsync(fixture.Host, ownerId)).Owner.Status.ShouldBe(OwnerStatus.Pending);
    }

    [Fact]
    public async Task MalformedJson_Is400() =>
        await fixture.Host.Scenario(s =>
        {
            s.Post.Url($"/v1/identity/owners/{Guid.CreateVersion7()}/verification");
            s.Body.TextIs("{ not json");
            s.WithRequestHeader("Content-Type", "application/json");
            s.StatusCodeShouldBe(400);
        });

    private static Task<IScenarioResult> VerifyAsync(IAlbaHost host, Guid ownerId, string code, int status) =>
        host.Scenario(s =>
        {
            s.Post.Json(new { code }).ToUrl($"/v1/identity/owners/{ownerId}/verification");
            s.StatusCodeShouldBe(status);
        });

    private static async Task<string[]> ErrorCodesAsync(IScenarioResult result)
    {
        var body = await result.ReadAsJsonAsync<JsonElement>();
        return [.. body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()!)];
    }

    private static string HashOf(IAlbaHost host, string secret) => host.Services.GetRequiredService<ISecretHasher>().Hash(secret);

    /// <summary>A pending owner holding the email code <see cref="Code"/>, as registration leaves one.</summary>
    private static async Task<Guid> SeedPendingOwnerAsync(IAlbaHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        var clock = services.GetRequiredService<TimeProvider>();

        var email = Email.Create($"owner-{Guid.NewGuid():N}@example.com").Value;
        var principal = Principal.RegisterOwner(PersonName.Create("Ana").Value, email, "password-hash", Locale.Parse("en"), clock.GetUtcNow());
        principal.IssueCode(CodePurpose.EmailVerify, HashOf(host, Code), clock.GetUtcNow()).IsSuccess.ShouldBeTrue();

        var repository = services.GetRequiredService<IPrincipalRepository>();
        repository.Add(principal);
        (await repository.SaveAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        return principal.Id;
    }
}
