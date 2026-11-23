using System.Diagnostics.Metrics;
using Frappe.Caching;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using ZiggyCreatures.Caching.Fusion;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>
/// The cache of several replicas on one real Valkey: values shared through it under the instance name, and tag
/// invalidations that reach every replica through the backplane, run once more after a delay (on a fake clock).
/// </summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class SharedCacheTests(ValkeyServer server)
{
    private const string Key = "orders:1";
    private const string Tag = "orders";
    private static readonly Instant PlacedAt = Instant.FromUtc(2030, 1, 2, 3, 4, 5);

    [Test]
    public async Task A_value_cached_by_one_replica_is_read_by_another_from_Valkey_under_the_instance_name()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var writer = CachingHost.Build(server.ConnectionString, instance);
        await using var reader = CachingHost.Build(server.ConnectionString, instance);
        await writer.GetRequiredService<IFusionCache>().SetAsync(Key, PlacedAt);
        await WaitUntil.TrueAsync(async () => (await server.KeysAsync($"*{instance}:*")).Count > 0,
            async () => $"the value never reached Valkey; keys: {string.Join(", ", await server.KeysAsync("*"))}");

        // Act
        var read = await reader.GetRequiredService<IFusionCache>().TryGetAsync<Instant>(Key);

        // Assert
        var keys = await server.KeysAsync($"*{instance}:*");
        using (Assert.Multiple())
        {
            await Assert.That(read.HasValue).IsTrue();
            await Assert.That(read.Value).IsEqualTo(PlacedAt);
            await Assert.That(keys.Any(key => key.EndsWith($"{instance}:{Key}", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task A_replica_with_another_instance_name_on_the_same_Valkey_sees_none_of_it()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var writer = CachingHost.Build(server.ConnectionString, instance);
        await using var stranger = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        await writer.GetRequiredService<IFusionCache>().SetAsync(Key, PlacedAt);
        await WaitUntil.TrueAsync(async () => (await server.KeysAsync($"*{instance}:*")).Count > 0,
            async () => $"the value never reached Valkey; keys: {string.Join(", ", await server.KeysAsync("*"))}");

        // Act
        var read = await stranger.GetRequiredService<IFusionCache>().TryGetAsync<Instant>(Key);

        // Assert
        await Assert.That(read.HasValue).IsFalse();
    }

    [Test]
    public async Task Invalidating_a_tag_in_one_replica_evicts_the_copy_another_replica_holds()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var writer = CachingHost.Build(server.ConnectionString, instance);
        await using var reader = CachingHost.Build(server.ConnectionString, instance);
        var readerCache = reader.GetRequiredService<IFusionCache>();
        await readerCache.SetAsync(Key, PlacedAt, tags: [Tag]);

        // Act
        await writer.GetRequiredService<CacheInvalidator>().InvalidateAsync(Tag, CancellationToken.None);

        // Assert
        await WaitUntil.TrueAsync(async () => !(await readerCache.TryGetAsync<Instant>(Key)).HasValue, "the other replica kept its copy");
        await Assert.That((await readerCache.TryGetAsync<Instant>(Key)).HasValue).IsFalse();
    }

    [Test]
    public async Task The_second_invalidation_evicts_a_copy_cached_right_after_the_first()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var replica = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName(),
            services => services.AddSingleton<TimeProvider>(time));
        var cache = replica.GetRequiredService<IFusionCache>();
        await cache.SetAsync(Key, PlacedAt, tags: [Tag]);
        await replica.GetRequiredService<CacheInvalidator>().InvalidateAsync(Tag, CancellationToken.None);
        await cache.SetAsync(Key, PlacedAt, tags: [Tag]);

        // Act
        time.Advance(CacheInvalidator.SecondInvalidationDelay);

        // Assert
        await WaitUntil.TrueAsync(async () => !(await cache.TryGetAsync<Instant>(Key)).HasValue, "the stale copy survived the second invalidation");
        await Assert.That((await cache.TryGetAsync<Instant>(Key)).HasValue).IsFalse();
    }

    [Test]
    public async Task The_second_invalidation_waits_for_its_delay()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var replica = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName(),
            services => services.AddSingleton<TimeProvider>(time));
        var cache = replica.GetRequiredService<IFusionCache>();
        await replica.GetRequiredService<CacheInvalidator>().InvalidateAsync(Tag, CancellationToken.None);
        await cache.SetAsync(Key, PlacedAt, tags: [Tag]);

        // Act
        time.Advance(CacheInvalidator.SecondInvalidationDelay - TimeSpan.FromMilliseconds(1));

        // Assert
        var read = await cache.TryGetAsync<Instant>(Key);
        using (Assert.Multiple())
        {
            await Assert.That(read.HasValue).IsTrue();
            await Assert.That(read.Value).IsEqualTo(PlacedAt);
        }
    }

    [Test]
    public async Task Disposing_the_invalidator_cancels_its_pending_second_invalidation()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var replica = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName(),
            services => services.AddSingleton<TimeProvider>(time));
        var cache = replica.GetRequiredService<IFusionCache>();
        var invalidator = replica.GetRequiredService<CacheInvalidator>();
        await invalidator.InvalidateAsync(Tag, CancellationToken.None);
        await cache.SetAsync(Key, PlacedAt, tags: [Tag]);
        await invalidator.DisposeAsync();

        // Act
        time.Advance(CacheInvalidator.SecondInvalidationDelay);

        // Assert
        var read = await cache.TryGetAsync<Instant>(Key);
        using (Assert.Multiple())
        {
            await Assert.That(read.HasValue).IsTrue();
            await Assert.That(time.GetUtcNow() - time.Start).IsEqualTo(CacheInvalidator.SecondInvalidationDelay);
        }
    }

    [Test]
    public async Task A_failed_second_invalidation_is_logged_as_a_warning_naming_the_tag()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var logs = new CapturedLogs();
        await using var replica = CachingHost.Build(
            server.ConnectionString,
            CachingHost.NewInstanceName(),
            services =>
            {
                services.AddSingleton<TimeProvider>(time);
                services.AddLogging(logging => logging.AddProvider(logs));
            });
        var cache = replica.GetRequiredService<IFusionCache>();
        await replica.GetRequiredService<CacheInvalidator>().InvalidateAsync(Tag, CancellationToken.None);
        cache.Dispose();

        // Act
        time.Advance(CacheInvalidator.SecondInvalidationDelay);

        // Assert
        await WaitUntil.TrueAsync(() => logs.Entries.Any(entry => entry.Category == typeof(CacheInvalidator).FullName),
            "the failed invalidation was never logged");
        var logged = logs.Entries.Single(entry => entry.Category == typeof(CacheInvalidator).FullName);
        using (Assert.Multiple())
        {
            await Assert.That(logged.Level).IsEqualTo(LogLevel.Warning);
            await Assert.That(logged.Message).Contains($"cache tag {Tag} failed");
            await Assert.That(logged.Exception).IsTypeOf<ObjectDisposedException>();
        }
    }

    [Test]
    public async Task A_failed_second_invalidation_is_counted()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var failures = 0L;
        string? unit = null;
        string? description = null;
        await using var replica = CachingHost.Build(
            server.ConnectionString,
            CachingHost.NewInstanceName(),
            services => services.AddSingleton<TimeProvider>(time));
        var meters = replica.GetRequiredService<IMeterFactory>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            // Only this replica's meter: other tests of the process fail invalidations of their own.
            if (ReferenceEquals(instrument.Meter.Scope, meters) && instrument.Name == CachingMetrics.FailedMetric)
            {
                unit = instrument.Unit;
                description = instrument.Description;
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref failures, measurement));
        listener.Start();
        var cache = replica.GetRequiredService<IFusionCache>();
        await replica.GetRequiredService<CacheInvalidator>().InvalidateAsync(Tag, CancellationToken.None);
        cache.Dispose();

        // Act
        time.Advance(CacheInvalidator.SecondInvalidationDelay);

        // Assert
        await WaitUntil.TrueAsync(() => Interlocked.Read(ref failures) > 0, "the failed invalidation was never counted");
        using (Assert.Multiple())
        {
            await Assert.That(Interlocked.Read(ref failures)).IsEqualTo(1);
            await Assert.That(unit).IsEqualTo("{invalidation}");
            await Assert.That(description).IsEqualTo("Delayed cache invalidations that failed.");
        }
    }

    [Test]
    public async Task A_blank_tag_is_refused_before_anything_is_invalidated()
    {
        // Arrange
        await using var replica = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var invalidator = replica.GetRequiredService<CacheInvalidator>();

        // Act
        var refused = await Assert.ThrowsAsync<ArgumentException>(async () => await invalidator.InvalidateAsync(" ", CancellationToken.None));

        // Assert
        await Assert.That(refused!.ParamName).IsEqualTo("tag");
    }
}
