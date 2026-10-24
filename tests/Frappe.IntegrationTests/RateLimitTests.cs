using System.Net;
using Alba;
using Frappe.Identity.Application.Security;
using Frappe.SharedKernel.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>
/// Valkey-independent backstops: an in-process per-IP rate limiter on the anonymous auth endpoints, and a bounded
/// wait for a password-hashing slot. Both answer the one problem shape.
/// </summary>
public sealed class RateLimitTests(ApiFixture fixture)
{
    private const string Sessions = "/v1/identity/sessions";

    [Fact]
    public async Task AnonymousAuthEndpoints_AreRateLimitedPerIp_WithTheProblemShape()
    {
        await using var host = await fixture.CreateHostAsync(configureSettings: s => s["Platform:RateLimiting:PermitLimit"] = "3");
        var ip = NewIp();

        for (var i = 0; i < 3; i++)
            await LogInAsync(host, ip, 401);
        var limited = await LogInAsync(host, ip, 429);

        await limited.ShouldBeProblemAsync(429, "platform.rate_limited");
        int.Parse(limited.Context.Response.Headers[HeaderNames.RetryAfter].ToString(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThan(0);
        await LogInAsync(host, NewIp(), 401);
    }

    [Fact]
    public async Task RateLimit_CoversRegistrationAndPasswordResets_Too()
    {
        await using var host = await fixture.CreateHostAsync(configureSettings: s => s["Platform:RateLimiting:PermitLimit"] = "1");
        var ip = NewIp();

        await host.Scenario(s =>
        {
            s.Post.Json(new { email = "x@example.test" }).ToUrl("/v1/identity/password-resets");
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = ip);
            s.IgnoreStatusCode();
        });
        var limited = await host.Scenario(s =>
        {
            s.Post.Json(new { name = "Ana", email = "y@example.test", password = "long enough password" }).ToUrl("/v1/identity/owners");
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = ip);
            s.StatusCodeShouldBe(429);
        });

        await limited.ShouldBeProblemAsync(429, "platform.rate_limited");
    }

    [Fact]
    public async Task ABusyPasswordHasher_Answers503_WithTheProblemShape()
    {
        await using var host = await fixture.CreateHostAsync(services => services.AddSingleton<IPasswordHasher, BusyHasher>());

        var result = await LogInAsync(host, NewIp(), 503, "busy@example.test");

        var body = await result.ShouldBeProblemAsync(503, "identity.password_hashing.busy");
        result.Context.Response.Headers[HeaderNames.RetryAfter].ToString().ShouldBe("1");
        body.GetProperty("status").GetInt32().ShouldBe(503);
    }

    private static Task<IScenarioResult> LogInAsync(IAlbaHost host, IPAddress ip, int status, string email = "nobody@example.test") =>
        host.Scenario(s =>
        {
            s.Post.Json(new { email, password = "wrong password!!" }).ToUrl(Sessions);
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = ip);
            s.StatusCodeShouldBe(status);
        });

    private static IPAddress NewIp() => new(Random.Shared.NextInt64(0x0B000001, 0x0BFFFFFE));

    private sealed class BusyHasher : IPasswordHasher
    {
        public Task<string> HashAsync(string password, CancellationToken cancellationToken) => throw Busy();
        public Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken) => throw Busy();
        public bool NeedsRehash(string passwordHash) => false;

        private static UnavailableException Busy() =>
            new(Frappe.Identity.Domain.IdentityErrors.PasswordHashingBusy.WithRetryAfter(TimeSpan.FromSeconds(1)));
    }
}
