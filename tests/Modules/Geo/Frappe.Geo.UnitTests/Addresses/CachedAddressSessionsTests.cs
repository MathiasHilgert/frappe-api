using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Infrastructure.Addresses;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>The marker of an open search session: opened once, closed once, and gone after the provider's session lifetime.</summary>
public sealed class CachedAddressSessionsTests
{
    [Test]
    public async Task A_session_opens_once_and_closes_once()
    {
        using var cache = new FusionCache(new FusionCacheOptions());
        var sessions = new CachedAddressSessions(cache);
        var session = AddressSession.New();

        await Assert.That(await sessions.OpenAsync("fake", session, TimeSpan.FromMinutes(1), CancellationToken.None)).IsTrue();
        await Assert.That(await sessions.OpenAsync("fake", session, TimeSpan.FromMinutes(1), CancellationToken.None)).IsFalse();
        await Assert.That(await sessions.CloseAsync("other", session, CancellationToken.None)).IsFalse();
        await Assert.That(await sessions.CloseAsync("fake", session, CancellationToken.None)).IsTrue();
        await Assert.That(await sessions.CloseAsync("fake", session, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task A_session_left_open_past_its_lifetime_no_longer_closes()
    {
        using var cache = new FusionCache(new FusionCacheOptions());
        var sessions = new CachedAddressSessions(cache);
        var session = AddressSession.New();

        await sessions.OpenAsync("fake", session, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        await Assert.That(await sessions.CloseAsync("fake", session, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task A_session_opened_on_one_replica_closes_on_another_through_the_shared_cache()
    {
        var shared = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var a = Replica(shared);
        using var b = Replica(shared);
        var session = AddressSession.New();

        await Assert.That(await new CachedAddressSessions(a).OpenAsync("fake", session, TimeSpan.FromMinutes(1), CancellationToken.None)).IsTrue();
        await Assert.That(await new CachedAddressSessions(b).OpenAsync("fake", session, TimeSpan.FromMinutes(1), CancellationToken.None)).IsFalse();
        await Assert.That(await new CachedAddressSessions(b).CloseAsync("fake", session, CancellationToken.None)).IsTrue();
    }

    [Test]
    public async Task A_session_expired_on_one_replica_does_not_close_on_another()
    {
        var shared = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var a = Replica(shared);
        using var b = Replica(shared);
        var session = AddressSession.New();

        await new CachedAddressSessions(a).OpenAsync("fake", session, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        await Assert.That(await new CachedAddressSessions(b).OpenAsync("fake", session, TimeSpan.FromMilliseconds(300), CancellationToken.None)).IsFalse();
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        await Assert.That(await new CachedAddressSessions(b).CloseAsync("fake", session, CancellationToken.None)).IsFalse();
    }

    /// <summary>A replica's cache: its own memory over the shared distributed cache, with the platform's default entry options.</summary>
    private static FusionCache Replica(IDistributedCache shared)
    {
        var cache = new FusionCache(new FusionCacheOptions
        {
            DefaultEntryOptions = new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(2),
                JitterMaxDuration = TimeSpan.FromSeconds(30),
                AllowBackgroundDistributedCacheOperations = true,
            },
        });
        cache.SetupDistributedCache(shared, new FusionCacheSystemTextJsonSerializer());
        return cache;
    }
}
