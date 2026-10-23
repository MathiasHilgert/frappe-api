using Frappe.Identity.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>The Valkey attempt limiter against a real Valkey: counts per key, blocks at the limit, never locks.</summary>
public sealed class AttemptLimiterTests(ApiFixture fixture)
{
    private static readonly AttemptPolicy ThreePerMinute = new("test-three", 3, TimeSpan.FromMinutes(1));

    private IAttemptLimiter Limiter => fixture.Host.Services.GetRequiredService<IAttemptLimiter>();

    [Fact]
    public async Task Check_UnderLimit_AllowsWithRemaining()
    {
        var key = UniqueKey();
        await Limiter.RecordAsync(ThreePerMinute, key, CancellationToken.None);

        var decision = await Limiter.CheckAsync(ThreePerMinute, key, CancellationToken.None);

        decision.IsAllowed.ShouldBeTrue();
        decision.Remaining.ShouldBe(2);
        decision.RetryAfter.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Check_AtLimit_BlocksWithRetryAfterWithinTheWindow()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            (await Limiter.RecordAsync(ThreePerMinute, key, CancellationToken.None)).IsAllowed.ShouldBeTrue();

        var decision = await Limiter.CheckAsync(ThreePerMinute, key, CancellationToken.None);

        decision.IsAllowed.ShouldBeFalse();
        decision.Remaining.ShouldBe(0);
        decision.RetryAfter.ShouldBeGreaterThan(TimeSpan.FromSeconds(50));
        decision.RetryAfter.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Record_BeyondLimit_IsNotAllowed()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, CancellationToken.None);

        var fourth = await Limiter.RecordAsync(ThreePerMinute, key, CancellationToken.None);

        fourth.IsAllowed.ShouldBeFalse();
        fourth.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Check_AfterWindowExpires_AllowsAgain()
    {
        var shortWindow = new AttemptPolicy("test-short", 1, TimeSpan.FromMilliseconds(500));
        var key = UniqueKey();
        await Limiter.RecordAsync(shortWindow, key, CancellationToken.None);
        (await Limiter.CheckAsync(shortWindow, key, CancellationToken.None)).IsAllowed.ShouldBeFalse();

        await Task.Delay(TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);

        (await Limiter.CheckAsync(shortWindow, key, CancellationToken.None)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Reset_ClearsTheCount()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, CancellationToken.None);

        await Limiter.ResetAsync(ThreePerMinute, key, CancellationToken.None);

        (await Limiter.CheckAsync(ThreePerMinute, key, CancellationToken.None)).Remaining.ShouldBe(3);
    }

    [Fact]
    public async Task Keys_AreIndependent_AndCaseInsensitive()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, CancellationToken.None);

        (await Limiter.CheckAsync(ThreePerMinute, key.ToUpperInvariant(), CancellationToken.None)).IsAllowed.ShouldBeFalse();
        (await Limiter.CheckAsync(ThreePerMinute, UniqueKey(), CancellationToken.None)).IsAllowed.ShouldBeTrue();
        (await Limiter.CheckAsync(ThreePerMinute with { Name = "test-other" }, key, CancellationToken.None))
            .IsAllowed.ShouldBeTrue();
    }

    private static string UniqueKey() => $"owner-{Ulid()}@example.com";

    private static string Ulid() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
