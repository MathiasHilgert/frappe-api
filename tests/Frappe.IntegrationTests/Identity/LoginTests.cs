using System.Net;
using System.Text.Json;
using Alba;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests.Identity;

/// <summary>POST /v1/identity/sessions, DELETE /v1/identity/sessions/current and GET /v1/identity/me.</summary>
public sealed class LoginTests(ApiFixture fixture)
{
    private const string Sessions = "/v1/identity/sessions";
    private const string CookieName = ApiFixture.SessionCookieName;

    [Fact]
    public async Task ValidCredentials_OpenASession_WithCookieAndToken()
    {
        var clock = TestTime.NewClock();
        await using var host = await fixture.CreateHostAsync(services => services.AddSingleton<TimeProvider>(clock));
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(host, email);

        var result = await LogInAsync(email.ToUpperInvariant(), OwnerSeeder.Password, NewIp(), 201, host);

        var body = await result.ReadAsJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString().ShouldNotBeNull();
        body.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(clock.GetUtcNow() + Session.AbsoluteLifetime);
        var cookie = result.Context.Response.Headers[HeaderNames.SetCookie].ToString();
        cookie.ShouldStartWith($"{CookieName}={token}");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);

        (await SessionSeeder.LoadAsync(host, owner.Id)).Sessions.Count.ShouldBe(1);
        await MeAsync(s => s.WithRequestHeader("Authorization", $"Bearer {token}"), 200, host);
        await MeAsync(s => s.WithRequestHeader("Cookie", $"{CookieName}={token}"), 200, host);
    }

    [Theory]
    [InlineData("wrong password!!")]
    [InlineData("")]
    public async Task WrongPassword_IsAGeneric401(string password)
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email);

        var unknown = await (await LogInAsync(OwnerSeeder.NewEmail(), OwnerSeeder.Password, NewIp(), 401))
            .ShouldBeProblemAsync(401, "identity.credentials.invalid");
        var wrong = await (await LogInAsync(email, password, NewIp(), 401)).ShouldBeProblemAsync(401, "identity.credentials.invalid");

        wrong.GetProperty("code").GetString().ShouldBe("identity.credentials.invalid");
        wrong.GetProperty("code").GetString().ShouldBe(unknown.GetProperty("code").GetString());
        wrong.GetProperty("detail").GetString().ShouldBe(unknown.GetProperty("detail").GetString());
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task MalformedEmail_IsTheSameGeneric401() =>
        await LogInAsync("not-an-email", OwnerSeeder.Password, NewIp(), 401);

    [Fact]
    public async Task PendingOwner_WithTheRightPassword_IsToldToVerify()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email, OwnerStatus.Pending);

        var body = await (await LogInAsync(email, OwnerSeeder.Password, NewIp(), 403)).ShouldBeProblemAsync(403, "identity.account.pending");

        body.GetProperty("ownerId").GetGuid().ShouldBe(owner.Id);
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task PendingOwner_WithAWrongPassword_IsTheGeneric401()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email, OwnerStatus.Pending);

        await LogInAsync(email, "wrong password!!", NewIp(), 401);
    }

    [Fact]
    public async Task SuspendedOwner_IsRefused()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email, OwnerStatus.Suspended);

        var body = (await BodyAsync(LogInAsync(email, OwnerSeeder.Password, NewIp(), 403)));

        body.GetProperty("code").GetString().ShouldBe("identity.account.suspended");
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task TenFailuresFromOneIp_ThrottleThatEmailFromThatIp_EvenForTheRightPassword_WithRetryAfter()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var ip = NewIp();

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit; i++)
            await LogInAsync(email, "wrong password!!", ip, 401);

        var result = await LogInAsync(email, OwnerSeeder.Password, ip, 429);
        int.Parse(result.Context.Response.Headers[HeaderNames.RetryAfter].ToString(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeInRange(1, (int)AttemptPolicies.LoginFailuresPerEmailAndIp.Window.TotalSeconds);
        await result.ShouldBeProblemAsync(429, "identity.attempts.too_many");
    }

    [Fact]
    public async Task AnAttackerFromOneIp_CannotLockTheOwnerOut()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var attacker = NewIp();
        using var throttled = new MeterCounter("Frappe.Identity", "frappe.identity.login.throttled");

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit + 5; i++)
            await LogInAsync(email, "wrong password!!", attacker, i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit ? 401 : 429);

        await LogInAsync(email, OwnerSeeder.Password, NewIp(), 201);
        throttled.Total.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task FailuresSpreadAcrossManyIps_HitTheEmailCeiling()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmail.Limit; i++)
            await LogInAsync(email, "wrong password!!", NewIp(), 401);

        await LogInAsync(email, OwnerSeeder.Password, NewIp(), 429);
    }

    [Fact]
    public async Task ADistributedAttack_CannotLockTheOwnerOut_FromAnAddressTheyAlreadySignedInFrom()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var home = NewIp();
        await LogInAsync(email, OwnerSeeder.Password, home, 201);

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmail.Limit; i++)
            await LogInAsync(email, "wrong password!!", NewIp(), 401);

        await LogInAsync(email, OwnerSeeder.Password, NewIp(), 429);
        await LogInAsync(email, OwnerSeeder.Password, home, 201);
    }

    [Fact]
    public async Task ASuccessfulSignIn_RemembersItsOrigin_OnlyAfterCommit_AsACascadedCommand()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(
            new Frappe.Identity.Application.Sessions.LogIn(email, OwnerSeeder.Password, "198.51.100.9", SessionSurface.Api));

        tracked.Sent.SingleMessage<Frappe.Identity.Application.Sessions.RememberSignInOrigin>()
            .ShouldBe(new Frappe.Identity.Application.Sessions.RememberSignInOrigin(email, "198.51.100.9"));
    }

    [Fact]
    public async Task AKnownAddress_StillHasItsOwnTightBudget()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var shared = NewIp();
        await LogInAsync(email, OwnerSeeder.Password, shared, 201);

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit; i++)
            await LogInAsync(email, "wrong password!!", shared, 401);

        await LogInAsync(email, OwnerSeeder.Password, shared, 429);
    }

    [Fact]
    public async Task Ipv6Addresses_InOneSlash64_ShareOneBudget_AndTheNextPrefixDoesNot()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var prefix = $"2001:db8:{Random.Shared.Next(0x10000):x}:{Random.Shared.Next(0x10000):x}";

        for (var i = 1; i <= AttemptPolicies.LoginFailuresPerEmailAndIp.Limit; i++)
            await LogInAsync(email, "wrong password!!", IPAddress.Parse($"{prefix}:{i:x}::{i:x}"), 401);

        await LogInAsync(email, OwnerSeeder.Password, IPAddress.Parse($"{prefix}:ffff:ffff:ffff:ffff"), 429);
        await LogInAsync(email, OwnerSeeder.Password, IPAddress.Parse($"2001:db8:ffff:{Random.Shared.Next(0x10000):x}::1"), 201);
    }

    [Fact]
    public void TheEmailCeiling_IsFarAboveThePerIpBudget() =>
        AttemptPolicies.LoginFailuresPerEmail.Limit.ShouldBeGreaterThanOrEqualTo(5 * AttemptPolicies.LoginFailuresPerEmailAndIp.Limit);

    [Fact]
    public async Task AParallelBurstOfWrongPasswords_GetsOnlyTheLimitOfTries()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var ip = NewIp();

        var statuses = await Burst.StatusesAsync(fixture.Host, s =>
        {
            s.Post.Json(new { email, password = "wrong password!!" }).ToUrl(Sessions);
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = ip);
        });

        statuses.Count(s => s == 401).ShouldBe(AttemptPolicies.LoginFailuresPerEmailAndIp.Limit);
        statuses.Count(s => s == 429).ShouldBe(Burst.Size - AttemptPolicies.LoginFailuresPerEmailAndIp.Limit);
        await LogInAsync(email, OwnerSeeder.Password, ip, 429);
    }

    [Fact]
    public async Task ASuccessfulLogin_WithAnOutdatedHash_StoresAHashAtTheCurrentCost()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email);
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPrincipalRepository>();
            var principal = (await repository.GetByIdAsync(owner.Id, CancellationToken.None)).ShouldNotBeNull();
            principal.RehashPassword(FastPasswordHasher.LegacyHash(OwnerSeeder.Password));
            (await repository.SaveAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await LogInAsync(email, OwnerSeeder.Password, NewIp(), 201);

        var stored = (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Owner.PasswordHash;
        var hasher = fixture.Host.Services.GetRequiredService<IPasswordHasher>();
        hasher.NeedsRehash(stored).ShouldBeFalse();
        (await hasher.VerifyAsync(OwnerSeeder.Password, stored, CancellationToken.None)).ShouldBeTrue();
        await LogInAsync(email, OwnerSeeder.Password, NewIp(), 201);
    }

    [Fact]
    public async Task ASuccessfulLogin_ClearsThatIpsFailuresForTheEmail()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var ip = NewIp();

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit - 1; i++)
            await LogInAsync(email, "wrong password!!", ip, 401);
        await LogInAsync(email, OwnerSeeder.Password, ip, 201);
        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit - 1; i++)
            await LogInAsync(email, "wrong password!!", ip, 401);

        await LogInAsync(email, OwnerSeeder.Password, ip, 201);
    }

    /// <summary>
    /// The right password on a suspended (or pending) account proves the caller holds the password, so it clears that
    /// email and IP's failures like any proof would; it opens no session and does not make the origin known.
    /// </summary>
    [Fact]
    public async Task TheRightPassword_OnASuspendedAccount_ClearsThatIpsFailures_ButRemembersNoOrigin()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email, OwnerStatus.Suspended);
        var ip = NewIp();

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit - 1; i++)
            await LogInAsync(email, "wrong password!!", ip, 401);
        await LogInAsync(email, OwnerSeeder.Password, ip, 403);
        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit - 1; i++)
            await LogInAsync(email, "wrong password!!", ip, 401);
        await LogInAsync(email, OwnerSeeder.Password, ip, 403);

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(
            new Frappe.Identity.Application.Sessions.LogIn(email, OwnerSeeder.Password, "198.51.100.10", SessionSurface.Api));
        tracked.Sent.MessagesOf<Frappe.Identity.Application.Sessions.RememberSignInOrigin>().ShouldBeEmpty();
    }

    [Fact]
    public async Task EmailCase_DoesNotGiveAnotherBudget()
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        var ip = NewIp();

        for (var i = 0; i < AttemptPolicies.LoginFailuresPerEmailAndIp.Limit; i++)
            await LogInAsync(i % 2 == 0 ? email : email.ToUpperInvariant(), "wrong password!!", ip, 401);

        await LogInAsync($"  {email.ToUpperInvariant()} ", OwnerSeeder.Password, ip, 429);
    }

    [Fact]
    public async Task FailuresFromOneIp_ThrottleThatIp_AcrossEmails()
    {
        var ip = NewIp();
        for (var i = 0; i < AttemptPolicies.LoginFailuresPerIp.Limit; i++)
            await LogInAsync(OwnerSeeder.NewEmail(), "wrong password!!", ip, 401);

        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);
        await LogInAsync(email, OwnerSeeder.Password, ip, 429);
        await LogInAsync(email, OwnerSeeder.Password, NewIp(), 201);
    }

    [Fact]
    public async Task MalformedJson_IsABadRequest() =>
        await fixture.Host.Scenario(s =>
        {
            s.Post.Url(Sessions);
            s.Body.TextIs("{");
            s.ConfigureHttpContext(c => c.Request.ContentType = "application/json");
            s.StatusCodeShouldBe(400);
        });

    [Fact]
    public async Task Me_ReturnsTheOwnersProfile()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email);
        var token = (await BodyAsync(LogInAsync(email, OwnerSeeder.Password, NewIp(), 201))).GetProperty("token").GetString();

        var me = (await BodyAsync(MeAsync(s => s.WithRequestHeader("Authorization", $"Bearer {token}"), 200)));

        me.GetProperty("id").GetGuid().ShouldBe(owner.Id);
        me.GetProperty("name").GetString().ShouldBe("Ana");
        me.GetProperty("email").GetString().ShouldBe(email);
        me.GetProperty("status").GetString().ShouldBe("active");
        me.GetProperty("locale").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Me_Anonymous_IsUnauthorized() => await MeAsync(_ => { }, 401);

    [Fact]
    public async Task Logout_RevokesTheSession_AndClearsTheCookie()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email);
        var token = (await BodyAsync(LogInAsync(email, OwnerSeeder.Password, NewIp(), 201))).GetProperty("token").GetString();

        var result = await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/current");
            s.WithRequestHeader("Cookie", $"{CookieName}={token}");
            s.WithRequestHeader("X-Requested-With", "frappe");
            s.StatusCodeShouldBe(204);
        });

        var cleared = result.Context.Response.Headers[HeaderNames.SetCookie].ToString();
        cleared.ShouldStartWith($"{CookieName}=;");
        cleared.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Sessions.ShouldBeEmpty();
        await MeAsync(s => s.WithRequestHeader("Authorization", $"Bearer {token}"), 401);
    }

    [Fact]
    public async Task Logout_WithBearer_KeepsTheOwnersOtherSessions()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(fixture.Host, email);
        var first = (await BodyAsync(LogInAsync(email, OwnerSeeder.Password, NewIp(), 201))).GetProperty("token").GetString();
        var second = (await BodyAsync(LogInAsync(email, OwnerSeeder.Password, NewIp(), 201))).GetProperty("token").GetString();

        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/current");
            s.WithRequestHeader("Authorization", $"Bearer {first}");
            s.StatusCodeShouldBe(204);
        });

        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Sessions.Count.ShouldBe(1);
        await MeAsync(s => s.WithRequestHeader("Authorization", $"Bearer {second}"), 200);
    }

    [Fact]
    public async Task Logout_Anonymous_IsUnauthorized() =>
        await fixture.Host.Scenario(s =>
        {
            s.Delete.Url($"{Sessions}/current");
            s.StatusCodeShouldBe(401);
        });

    private Task<IScenarioResult> LogInAsync(
        string email, string password, IPAddress ip, int status, IAlbaHost? host = null) =>
        (host ?? fixture.Host).Scenario(s =>
        {
            s.Post.Json(new { email, password }).ToUrl(Sessions);
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = ip);
            s.StatusCodeShouldBe(status);
        });

    private Task<IScenarioResult> MeAsync(Action<Scenario> configure, int status, IAlbaHost? host = null) =>
        (host ?? fixture.Host).Scenario(s =>
        {
            s.Get.Url("/v1/identity/me");
            configure(s);
            s.StatusCodeShouldBe(status);
        });

    private static async Task<JsonElement> BodyAsync(Task<IScenarioResult> scenario) =>
        await (await scenario).ReadAsJsonAsync<JsonElement>();

    private static IPAddress NewIp() => new(Random.Shared.NextInt64(0x0A000001, 0x0AFFFFFE));
}
