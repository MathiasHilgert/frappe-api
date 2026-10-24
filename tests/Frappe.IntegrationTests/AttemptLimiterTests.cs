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
        await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        var decision = await Limiter.CheckAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        decision.IsAllowed.ShouldBeTrue();
        decision.Remaining.ShouldBe(2);
        decision.RetryAfter.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Check_AtLimit_BlocksWithRetryAfterWithinTheWindow()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            (await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();

        var decision = await Limiter.CheckAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

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
            await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        var fourth = await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        fourth.IsAllowed.ShouldBeFalse();
        fourth.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Check_AfterWindowExpires_AllowsAgain()
    {
        // Valkey owns the expiry (PEXPIRE), so no fake clock reaches it: a short window, polled until it lapses.
        var shortWindow = new AttemptPolicy("test-short", 1, TimeSpan.FromMilliseconds(100));
        var key = UniqueKey();
        await Limiter.RecordAsync(shortWindow, key, TestContext.Current.CancellationToken);
        (await Limiter.CheckAsync(shortWindow, key, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!(await Limiter.CheckAsync(shortWindow, key, TestContext.Current.CancellationToken)).IsAllowed)
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
    }

    [Fact]
    public async Task Reset_ClearsTheCount()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        await Limiter.ResetAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        (await Limiter.CheckAsync(ThreePerMinute, key, TestContext.Current.CancellationToken)).Remaining.ShouldBe(3);
    }

    [Fact]
    public async Task Keys_AreIndependent_AndCaseInsensitive()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        (await Limiter.CheckAsync(ThreePerMinute, key.ToUpperInvariant(), TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();
        (await Limiter.CheckAsync(ThreePerMinute, UniqueKey(), TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await Limiter.CheckAsync(ThreePerMinute with { Name = "test-other" }, key, TestContext.Current.CancellationToken))
            .IsAllowed.ShouldBeTrue();
    }

    private static string UniqueKey() => $"owner-{Ulid()}@example.com";

    private static string Ulid() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
