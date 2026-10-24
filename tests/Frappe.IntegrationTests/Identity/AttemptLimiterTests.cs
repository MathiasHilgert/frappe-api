using Frappe.Identity.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>The Valkey attempt limiter against a real Valkey: counts per key, blocks at the limit, never locks.</summary>
public sealed class AttemptLimiterTests(ApiFixture fixture)
{
    private static readonly AttemptPolicy ThreePerMinute = new("test-three", 3, TimeSpan.FromMinutes(1));

    private IAttemptLimiter Limiter => fixture.Host.Services.GetRequiredService<IAttemptLimiter>();

    [Fact]
    public async Task Reserve_UnderLimit_AllowsWithRemaining()
    {
        var key = UniqueKey();
        await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        var decision = await Limiter.ReserveAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        decision.IsAllowed.ShouldBeTrue();
        decision.Remaining.ShouldBe(1);
        decision.RetryAfter.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Reserve_AtLimit_BlocksWithRetryAfterWithinTheWindow()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            (await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();

        var decision = await Limiter.ReserveAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

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
    public async Task TheFirstAttempt_StartsAWindowThatValkeyExpires()
    {
        // Valkey owns the expiry (PEXPIRE) and no fake clock reaches it, so instead of waiting the window out the
        // test reads the expiry the first attempt set on the counter.
        var policy = new AttemptPolicy($"test-window-{Guid.NewGuid():N}", 3, TimeSpan.FromMinutes(1));
        await Limiter.RecordAsync(policy, UniqueKey(), TestContext.Current.CancellationToken);
        var valkey = await fixture.Host.Services.GetRequiredService<Frappe.Platform.Caching.ValkeyConnection>().GetAsync();
        var server = valkey.GetServer(valkey.GetEndPoints()[0]);
        var database = valkey.GetDatabase();

        var counter = server.Keys(database.Database, $"*{policy.Name}*").ShouldHaveSingleItem();
        var ttl = (await database.KeyTimeToLiveAsync(counter)).ShouldNotBeNull();

        ttl.ShouldBeGreaterThan(TimeSpan.Zero);
        ttl.ShouldBeLessThanOrEqualTo(policy.Window);
    }

    [Fact]
    public async Task Reset_ClearsTheCount()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        await Limiter.ResetAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        (await Limiter.ReserveAsync(ThreePerMinute, key, TestContext.Current.CancellationToken)).Remaining.ShouldBe(2);
    }

    [Fact]
    public async Task Keys_AreIndependent_AndCaseInsensitive()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            await Limiter.RecordAsync(ThreePerMinute, key, TestContext.Current.CancellationToken);

        (await Limiter.ReserveAsync(ThreePerMinute, key.ToUpperInvariant(), TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();
        (await Limiter.ReserveAsync(ThreePerMinute, UniqueKey(), TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await Limiter.ReserveAsync(ThreePerMinute with { Name = "test-other" }, key, TestContext.Current.CancellationToken))
            .IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Reserve_AParallelBurst_AdmitsExactlyTheLimit()
    {
        var key = UniqueKey();

        var decisions = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => Limiter.ReserveAsync(ThreePerMinute, key, CancellationToken.None)));

        decisions.Count(d => d.IsAllowed).ShouldBe(3);
        decisions.Where(d => !d.IsAllowed).ShouldAllBe(d => d.RetryAfter > TimeSpan.Zero);
        (await Limiter.ReserveAsync(ThreePerMinute, key, CancellationToken.None)).Remaining.ShouldBe(0);
    }

    [Fact]
    public async Task Reserve_AtTheLimit_RefusesWithoutCounting_AndReleaseGivesASlotBack()
    {
        var key = UniqueKey();
        for (var i = 0; i < 3; i++)
            (await Limiter.ReserveAsync(ThreePerMinute, key, CancellationToken.None)).IsAllowed.ShouldBeTrue();
        (await Limiter.ReserveAsync(ThreePerMinute, key, CancellationToken.None)).IsAllowed.ShouldBeFalse();

        await Limiter.ReleaseAsync(ThreePerMinute, key, CancellationToken.None);

        var again = await Limiter.ReserveAsync(ThreePerMinute, key, CancellationToken.None);
        again.IsAllowed.ShouldBeTrue();
        again.Remaining.ShouldBe(0);
    }

    [Fact]
    public async Task Release_OfAnUnknownKey_NeverGoesBelowZero()
    {
        var key = UniqueKey();

        await Limiter.ReleaseAsync(ThreePerMinute, key, CancellationToken.None);

        (await Limiter.ReserveAsync(ThreePerMinute, key, CancellationToken.None)).Remaining.ShouldBe(2);
    }

    private static string UniqueKey() => $"owner-{Ulid()}@example.com";

    private static string Ulid() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
