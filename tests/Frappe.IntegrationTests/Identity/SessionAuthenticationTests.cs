using System.Globalization;
using Alba;
using Frappe.Identity.Domain.Principals;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>
/// Owner sessions authenticate by cookie or Bearer token. No identity route exists yet, so an unknown route is the
/// probe: the deny-by-default fallback answers 401 to anonymous callers and 404 once a caller is authenticated.
/// </summary>
public sealed class SessionAuthenticationTests(ApiFixture fixture)
{
    private const string Probe = "/v1/does-not-exist";
    private const string CookieName = ApiFixture.SessionCookieName;

    [Fact]
    public async Task ValidCookie_Authenticates()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await GetAsync(fixture.Host, s => s.WithRequestHeader("Cookie", $"{CookieName}={token}"), 404);
    }

    [Fact]
    public async Task ValidBearer_Authenticates()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await GetAsync(fixture.Host, s => s.WithRequestHeader("Authorization", $"Bearer {token}"), 404);
    }

    [Fact]
    public async Task NoToken_IsUnauthorized() => await GetAsync(fixture.Host, _ => { }, 401);

    [Theory]
    [InlineData("Cookie", CookieName + "=not-a-real-token")]
    [InlineData("Authorization", "Bearer not-a-real-token")]
    [InlineData("Authorization", "Basic dXNlcjpwYXNz")]
    public async Task UnknownToken_IsUnauthorized(string header, string value) =>
        await GetAsync(fixture.Host, s => s.WithRequestHeader(header, value), 401);

    [Fact]
    public async Task SessionCookieSentTwice_IsUnauthorized_SoAShadowingCookieCannotWin()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await GetAsync(fixture.Host, s => s.WithRequestHeader("Cookie", $"{CookieName}=planted; {CookieName}={token}"), 401);
    }

    [Fact]
    public void DefaultCookieName_UsesTheHostPrefix() =>
        new Frappe.Identity.Infrastructure.Sessions.SessionCookieOptions().Name.ShouldBe("__Host-frappe_session");

    [Fact]
    public async Task RevokedSession_IsUnauthorized()
    {
        var (principal, session, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await SessionSeeder.RevokeAsync(fixture.Host, principal.Id, session.Id);

        await GetAsync(fixture.Host, Bearer(token), 401);
    }

    [Theory]
    [InlineData("Cookie", CookieName + "={0}")]
    [InlineData("Authorization", "Bearer {0}")]
    public async Task SuspendedOwnersSession_IsUnauthorized(string header, string format)
    {
        var (principal, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await SessionSeeder.SuspendAsync(fixture.Host, principal.Id);

        await GetAsync(fixture.Host, s => s.WithRequestHeader(header, string.Format(CultureInfo.InvariantCulture, format, token)), 401);
    }

    [Fact]
    public async Task IdleExpiredSession_IsUnauthorized()
    {
        var clock = new TestClock();
        await using var host = await ClockedHostAsync(clock);
        var (_, _, token) = await SessionSeeder.SeedAsync(host);

        clock.Advance(Session.IdleTimeout);

        await GetAsync(host, Bearer(token), 401);
    }

    [Fact]
    public async Task Use_MovesTheIdleExpiry()
    {
        var clock = new TestClock();
        await using var host = await ClockedHostAsync(clock);
        var (principal, _, token) = await SessionSeeder.SeedAsync(host);

        clock.Advance(Session.IdleTimeout - TimeSpan.FromHours(1));
        await GetAsync(host, Bearer(token), 404);
        (await SessionSeeder.LoadAsync(host, principal.Id)).Sessions[0].LastSeenAt.ShouldBe(clock.Now, TimeSpan.FromMilliseconds(1));

        clock.Advance(Session.IdleTimeout - TimeSpan.FromHours(1));
        await GetAsync(host, Bearer(token), 404);
    }

    [Fact]
    public async Task UseWithinTheTouchInterval_DoesNotWrite()
    {
        var clock = new TestClock();
        await using var host = await ClockedHostAsync(clock);
        var (principal, session, token) = await SessionSeeder.SeedAsync(host);

        clock.Advance(TimeSpan.FromMinutes(1));
        await GetAsync(host, Bearer(token), 404);

        (await SessionSeeder.LoadAsync(host, principal.Id)).Sessions[0].LastSeenAt
            .ShouldBe(session.LastSeenAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task AbsoluteExpiredSession_IsUnauthorizedEvenWhileInUse()
    {
        var clock = new TestClock();
        await using var host = await ClockedHostAsync(clock);
        var (_, _, token) = await SessionSeeder.SeedAsync(host);

        // Keep it busy every 10 hours until just short of 30 days, so only the absolute lifetime can end it.
        var step = TimeSpan.FromHours(10);
        while (clock.Now + step < new TestClock().Now + Session.AbsoluteLifetime)
        {
            clock.Advance(step);
            await GetAsync(host, Bearer(token), 404);
        }

        clock.Advance(step);

        await GetAsync(host, Bearer(token), 401);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task CookieOnUnsafeMethod_WithoutTheCsrfHeader_IsRejected(string method)
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await SendAsync(fixture.Host, method, s => s.WithRequestHeader("Cookie", $"{CookieName}={token}"), 401);
    }

    [Fact]
    public async Task CookieOnUnsafeMethod_WithTheCsrfHeader_IsAllowed()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await SendAsync(fixture.Host, "POST", s =>
        {
            s.WithRequestHeader("Cookie", $"{CookieName}={token}");
            s.WithRequestHeader("X-Requested-With", "frappe");
        }, 404);
    }

    [Fact]
    public async Task BearerOnUnsafeMethod_NeedsNoCsrfHeader()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await SendAsync(fixture.Host, "POST", Bearer(token), 404);
    }

    private static Action<Scenario> Bearer(string token) => s => s.WithRequestHeader("Authorization", $"Bearer {token}");

    private Task<IAlbaHost> ClockedHostAsync(TestClock clock) =>
        fixture.CreateHostAsync(services => services.AddSingleton<TimeProvider>(clock));

    private static Task GetAsync(IAlbaHost host, Action<Scenario> configure, int status) =>
        SendAsync(host, "GET", configure, status);

    private static async Task SendAsync(IAlbaHost host, string method, Action<Scenario> configure, int status) =>
        await host.Scenario(s =>
        {
            s.ConfigureHttpContext(c =>
            {
                c.Request.Method = method;
                c.Request.Path = Probe;
            });
            configure(s);
            s.StatusCodeShouldBe(status);
        });
}
