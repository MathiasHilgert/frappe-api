using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>
/// The anonymous limit counted in a store every replica shares, and what happens when that store fails: each replica
/// falls back to its own window instead of failing or waiting.
/// </summary>
public sealed class SharedRateLimitTests
{
    private static readonly TimeSpan StoreRemaining = TimeSpan.FromSeconds(42);

    /// <summary>One counter per key, as a shared store keeps it; every window reports <see cref="StoreRemaining"/>.</summary>
    private sealed class InMemoryStore : IRateLimitStore
    {
        private readonly ConcurrentDictionary<string, long> _counts = new();

        public ConcurrentBag<string> Keys { get; } = [];

        public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
        {
            Keys.Add(key);
            var count = _counts.AddOrUpdate(key, permits, (_, current) => current + permits);
            return ValueTask.FromResult(new RateLimitWindow(count, StoreRemaining));
        }
    }

    /// <summary>Every hit is over the limit, in a window that is closing right now.</summary>
    private sealed class ClosingStore : IRateLimitStore
    {
        public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RateLimitWindow(long.MaxValue, TimeSpan.Zero));
    }

    /// <summary>A rate-limit store that fails every call, as if Valkey were down.</summary>
    private sealed class FailingStore : IRateLimitStore
    {
        private int _calls;

        public int Calls => _calls;

        public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("The store is down.");
        }
    }

    /// <summary>Never answers; only the caller's token ends the wait.</summary>
    private sealed class SilentStore : IRateLimitStore
    {
        private int _calls;

        public int Calls => _calls;

        public async ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
            throw new UnreachableException();
        }
    }

    /// <summary>Fails the first call at once; later calls wait for <see cref="Release"/>, then fail or answer as told.</summary>
    private sealed class GatedStore(bool failAfterGate) : IRateLimitStore
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public bool FailFirstAtOnce { get; init; } = true;

        public int Calls => _calls;

        public void Release() => _gate.TrySetResult();

        public async ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1 && FailFirstAtOnce)
            {
                throw new InvalidOperationException("The store is down.");
            }

            await _gate.Task.WaitAsync(cancellationToken);
            return failAfterGate ? throw new InvalidOperationException("The store is down.") : new RateLimitWindow(1, StoreRemaining);
        }
    }

    /// <summary>A logger that only counts the warnings written to it.</summary>
    private sealed class WarningCounter : ILogger<SharedRateLimitStore>
    {
        private int _warnings;

        public int Warnings => _warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warnings);
            }
        }
    }

    private static IMeterFactory NewMeterFactory() =>
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    private static SharedRateLimitStore NewStore(
        IRateLimitStore store, TimeProvider clock, IMeterFactory? meters = null, ILogger<SharedRateLimitStore>? logger = null) =>
        new(store, clock, new RateLimitMetrics(meters ?? NewMeterFactory()), logger ?? NullLogger<SharedRateLimitStore>.Instance);

    /// <summary>Every measurement of the rate limit instruments of one meter factory, as (name, tag value, value).</summary>
    private sealed class RateLimitCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public RateLimitCapture(IMeterFactory meters)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, meters))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.Start();
        }

        public ConcurrentQueue<(string Name, string? Tag, double Value)> Measurements { get; } = new();

        public int Count(string name, string tag) => Measurements.Count(m => m.Name == name && m.Tag == tag);

        public void Dispose() => _listener.Dispose();

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            Measurements.Enqueue((instrument.Name, tags.Length == 0 ? null : tags[0].Value as string, value));
    }

    private static async Task<WebApplication> StartAsync(IRateLimitStore store, int permitLimit)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddFrappeHttp(new AnonymousRateLimit(permitLimit, TimeSpan.FromMinutes(10)))
            .AddSingleton(store);

        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapGroup("/v1").RequireRateLimiting(HttpRegistration.AnonymousPolicy).MapGet("/ping", () => "pong");
        await app.StartAsync();
        return app;
    }

    /// <summary>Runs synchronously up to the store's first wait, so calls start in order.</summary>
    private static async Task<System.Threading.RateLimiting.RateLimitLease> AcquireAsync(SharedFixedWindowRateLimiter limiter) =>
        await limiter.AcquireAsync(1);

    private static async Task<HttpResponseMessage> PingAsync(WebApplication app)
    {
        using var client = app.GetTestClient();
        return await client.GetAsync(new Uri("/v1/ping", UriKind.Relative));
    }

    [Test]
    public async Task Replicas_share_one_count_through_the_store()
    {
        // Arrange
        var store = new InMemoryStore();
        await using var replicaA = await StartAsync(store, permitLimit: 2);
        await using var replicaB = await StartAsync(store, permitLimit: 2);

        using var first = await PingAsync(replicaA);
        using var second = await PingAsync(replicaA);

        // Act
        using var third = await PingAsync(replicaB);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(third.Headers.RetryAfter?.Delta).IsEqualTo(StoreRemaining);
        }
    }

    [Test]
    public async Task The_store_key_names_the_policy_and_the_client()
    {
        // Arrange
        var store = new InMemoryStore();
        await using var app = await StartAsync(store, permitLimit: 2);

        // Act
        using var response = await PingAsync(app);

        // Assert
        await Assert.That(store.Keys.Single()).IsEqualTo($"{HttpRegistration.AnonymousPolicy}:unknown");
    }

    [Test]
    public async Task A_failing_store_leaves_each_replica_its_own_limit_and_is_not_asked_again_for_a_while()
    {
        // Arrange
        var store = new FailingStore();
        await using var app = await StartAsync(store, permitLimit: 2);

        using var first = await PingAsync(app);
        using var second = await PingAsync(app);

        // Act
        using var third = await PingAsync(app);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(third.Headers.RetryAfter?.Delta).IsNotNull();
            await Assert.That(store.Calls).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_store_that_does_not_answer_costs_a_request_at_most_its_timeout()
    {
        // Arrange
        var store = new SilentStore();
        await using var app = await StartAsync(store, permitLimit: 5);

        var watch = Stopwatch.StartNew();
        using var first = await PingAsync(app);
        using var second = await PingAsync(app);

        // Act
        watch.Stop();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
            await Assert.That(store.Calls).IsEqualTo(1);
        }
    }

    [Test]
    public async Task An_aborted_request_does_not_stop_asking_the_store()
    {
        // Arrange
        var store = new SilentStore();
        var shared = NewStore(store, TimeProvider.System);
        using var limiter = new SharedFixedWindowRateLimiter("anonymous:client", 2, TimeSpan.FromMinutes(1), shared, TimeProvider.System);
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();


        // Act
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => await limiter.AcquireAsync(1, aborted.Token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(shared.IsOpen).IsFalse();
        }
    }

    [Test]
    public async Task After_its_break_one_request_probes_the_store_while_the_others_count_locally()
    {
        // Arrange
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var store = new GatedStore(failAfterGate: false);
        var shared = NewStore(store, clock);
        using var limiter = new SharedFixedWindowRateLimiter("anonymous:client", 10, TimeSpan.FromMinutes(1), shared, clock);
        using var failed = await limiter.AcquireAsync(1);
        clock.Advance(SharedRateLimitStore.BreakDuration);

        var probe = AcquireAsync(limiter);
        var others = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AcquireAsync(limiter)));
        store.Release();
        using var probed = await probe;

        // Act
        using var afterwards = await limiter.AcquireAsync(1);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failed.IsAcquired).IsTrue();
            await Assert.That(others.All(lease => lease.IsAcquired)).IsTrue();
            await Assert.That(probed.IsAcquired).IsTrue();
            await Assert.That(shared.IsOpen).IsFalse();
            await Assert.That(store.Calls).IsEqualTo(3);
        }
    }

    [Test]
    public async Task A_burst_of_failing_requests_logs_the_outage_once()
    {
        // Arrange
        var logger = new WarningCounter();
        var store = new GatedStore(failAfterGate: true) { FailFirstAtOnce = false };
        var shared = NewStore(store, TimeProvider.System, logger: logger);
        using var limiter = new SharedFixedWindowRateLimiter("anonymous:client", 10, TimeSpan.FromMinutes(1), shared, TimeProvider.System);

        var burst = Enumerable.Range(0, 5).Select(_ => AcquireAsync(limiter)).ToArray();
        store.Release();

        // Act
        var leases = await Task.WhenAll(burst);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(leases.All(lease => lease.IsAcquired)).IsTrue();
            await Assert.That(store.Calls).IsEqualTo(5);
            await Assert.That(logger.Warnings).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_failed_probe_opens_the_breaker_for_another_break()
    {
        // Arrange
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var store = new FailingStore();
        var shared = NewStore(store, clock);
        using var limiter = new SharedFixedWindowRateLimiter("anonymous:client", 10, TimeSpan.FromMinutes(1), shared, clock);
        using var first = await limiter.AcquireAsync(1);
        clock.Advance(SharedRateLimitStore.BreakDuration);

        using var probe = await limiter.AcquireAsync(1);
        var openAfterProbe = shared.IsOpen;
        clock.Advance(SharedRateLimitStore.BreakDuration - TimeSpan.FromSeconds(1));
        using var duringBreak = await limiter.AcquireAsync(1);
        var callsDuringBreak = store.Calls;
        clock.Advance(TimeSpan.FromSeconds(1));

        // Act
        using var nextProbe = await limiter.AcquireAsync(1);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(openAfterProbe).IsTrue();
            await Assert.That(callsDuringBreak).IsEqualTo(2);
            await Assert.That(store.Calls).IsEqualTo(3);
            await Assert.That(new[] { first, probe, duringBreak, nextProbe }.All(lease => lease.IsAcquired)).IsTrue();
        }
    }

    [Test]
    public async Task A_failed_probe_still_rejects_beyond_the_local_limit_with_retry_after()
    {
        // Arrange
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var shared = NewStore(new FailingStore(), clock);
        using var limiter = new SharedFixedWindowRateLimiter("anonymous:client", 1, TimeSpan.FromMinutes(1), shared, clock);
        using var first = await limiter.AcquireAsync(1);
        clock.Advance(SharedRateLimitStore.BreakDuration);

        // Act
        using var probe = await limiter.AcquireAsync(1);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.IsAcquired).IsTrue();
            await Assert.That(probe.IsAcquired).IsFalse();
            await Assert.That(probe.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var wait)).IsTrue();
            await Assert.That(wait).IsGreaterThan(TimeSpan.Zero);
        }
    }

    [Test]
    public async Task A_window_closing_now_still_answers_retry_after_of_one_second()
    {
        // Arrange
        await using var app = await StartAsync(new ClosingStore(), permitLimit: 1);

        // Act
        using var limited = await PingAsync(app);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(limited.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(1));
        }
    }

    [Test]
    public async Task The_store_latency_and_every_request_counted_locally_are_measured()
    {
        // Arrange
        var meters = NewMeterFactory();
        using var capture = new RateLimitCapture(meters);
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56));
        var answering = NewStore(new InMemoryStore(), clock, meters);
        var failing = NewStore(new FailingStore(), clock, meters);
        using var shared = new SharedFixedWindowRateLimiter("anonymous:a", 10, TimeSpan.FromMinutes(1), answering, clock);
        using var local = new SharedFixedWindowRateLimiter("anonymous:b", 10, TimeSpan.FromMinutes(1), failing, clock);

        using var counted = await shared.AcquireAsync(1);
        using var failed = await local.AcquireAsync(1);

        // Act
        using var skipped = await local.AcquireAsync(1);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(capture.Count(RateLimitMetrics.StoreDurationMetric, RateLimitMetrics.Ok)).IsEqualTo(1);
            await Assert.That(capture.Count(RateLimitMetrics.StoreDurationMetric, RateLimitMetrics.Failed)).IsEqualTo(1);
            await Assert.That(capture.Count(RateLimitMetrics.CountedLocallyMetric, RateLimitMetrics.Failed)).IsEqualTo(1);
            await Assert.That(capture.Count(RateLimitMetrics.CountedLocallyMetric, RateLimitMetrics.Skipped)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_client_partition_can_be_evicted_once_used()
    {
        // Arrange
        var shared = NewStore(new InMemoryStore(), TimeProvider.System);
        using var limiter = new SharedFixedWindowRateLimiter("anonymous:client", 2, TimeSpan.FromMinutes(1), shared, TimeProvider.System);

        // Act
        using var lease = await limiter.AcquireAsync(1);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(lease.IsAcquired).IsTrue();
            await Assert.That(limiter.IdleDuration).IsNotNull();
        }
    }
}
