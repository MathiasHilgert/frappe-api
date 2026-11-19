using Frappe.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The idempotency store of a process without a shared one, and how <c>AddFrappeHttp</c> chooses it.</summary>
public sealed class InMemoryIdempotencyStoreTests
{
    /// <summary>A store a host registers for itself.</summary>
    private sealed class HostStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new IdempotencyClaim(IdempotencyOutcome.Claimed, Owner: "host"));

        public ValueTask<bool> CompleteAsync(string key, string owner, string fingerprint, StoredResponse response, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<bool> ReleaseAsync(string key, string owner, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static StoredResponse Answer() => new(201, new Dictionary<string, string[]>(), [1]);

    [Test]
    public async Task A_claim_that_is_never_completed_stops_holding_the_key_after_the_in_flight_lifetime()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var store = new InMemoryIdempotencyStore(clock);
        await store.ClaimAsync("key", "content", CancellationToken.None);
        var before = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Act
        clock.Advance(IdempotencyKey.InFlightLifetime + TimeSpan.FromSeconds(1));
        var after = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(after.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
        }
    }

    [Test]
    public async Task A_completed_key_replays_its_answer_and_a_released_one_is_free()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore(new FakeTimeProvider(Start));
        var done = await store.ClaimAsync("done", "content", CancellationToken.None);
        await store.CompleteAsync("done", done.Owner!, "content", Answer(), CancellationToken.None);
        var claimed = await store.ClaimAsync("freed", "content", CancellationToken.None);

        // Act
        await store.ReleaseAsync("freed", claimed.Owner!, CancellationToken.None);
        var replay = await store.ClaimAsync("done", "content", CancellationToken.None);
        var freed = await store.ClaimAsync("freed", "other", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(replay.Outcome).IsEqualTo(IdempotencyOutcome.Replay);
            await Assert.That(replay.Response!.Status).IsEqualTo(201);
            await Assert.That(freed.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
        }
    }

    [Test]
    public async Task A_request_whose_claim_expired_never_overwrites_the_key_claimed_again_by_a_retry()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var store = new InMemoryIdempotencyStore(clock);
        var slow = await store.ClaimAsync("key", "content", CancellationToken.None);
        clock.Advance(IdempotencyKey.InFlightLifetime + TimeSpan.FromSeconds(1));
        var retry = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Act
        var slowCompleted = await store.CompleteAsync("key", slow.Owner!, "content", new StoredResponse(201, new Dictionary<string, string[]>(), [1]), CancellationToken.None);
        var whileRetryRuns = await store.ClaimAsync("key", "content", CancellationToken.None);
        var retryCompleted = await store.CompleteAsync("key", retry.Owner!, "content", new StoredResponse(201, new Dictionary<string, string[]>(), [2]), CancellationToken.None);
        var replay = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(retry.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
            await Assert.That(retry.Owner).IsNotEqualTo(slow.Owner);
            await Assert.That(slowCompleted).IsFalse();
            await Assert.That(whileRetryRuns.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(retryCompleted).IsTrue();
            await Assert.That(replay.Response!.Body).IsEquivalentTo(new byte[] { 2 });
        }
    }

    [Test]
    public async Task A_request_whose_claim_expired_never_releases_the_claim_or_answer_of_a_retry()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var store = new InMemoryIdempotencyStore(clock);
        var slow = await store.ClaimAsync("running", "content", CancellationToken.None);
        var slowToo = await store.ClaimAsync("answered", "content", CancellationToken.None);
        clock.Advance(IdempotencyKey.InFlightLifetime + TimeSpan.FromSeconds(1));
        await store.ClaimAsync("running", "content", CancellationToken.None);
        var answered = await store.ClaimAsync("answered", "content", CancellationToken.None);
        await store.CompleteAsync("answered", answered.Owner!, "content", Answer(), CancellationToken.None);

        // Act
        var releasedRunning = await store.ReleaseAsync("running", slow.Owner!, CancellationToken.None);
        var releasedAnswered = await store.ReleaseAsync("answered", slowToo.Owner!, CancellationToken.None);
        var running = await store.ClaimAsync("running", "content", CancellationToken.None);
        var replay = await store.ClaimAsync("answered", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(releasedRunning).IsFalse();
            await Assert.That(releasedAnswered).IsFalse();
            await Assert.That(running.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(replay.Outcome).IsEqualTo(IdempotencyOutcome.Replay);
        }
    }

    [Test]
    public async Task A_claim_that_expired_with_no_retry_is_no_longer_owned()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var store = new InMemoryIdempotencyStore(clock);
        var slow = await store.ClaimAsync("key", "content", CancellationToken.None);
        clock.Advance(IdempotencyKey.InFlightLifetime + TimeSpan.FromSeconds(1));

        // Act
        var completed = await store.CompleteAsync("key", slow.Owner!, "content", Answer(), CancellationToken.None);
        var next = await store.ClaimAsync("key", "content", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(completed).IsFalse();
            await Assert.That(next.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
        }
    }

    [Test]
    public async Task Without_the_shared_cache_the_store_is_kept_in_memory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        using var provider = services.AddFrappeHttp().BuildServiceProvider();

        // Assert
        await Assert.That(provider.GetRequiredService<IIdempotencyStore>()).IsTypeOf<InMemoryIdempotencyStore>();
    }

    [Test]
    public async Task A_store_the_host_registered_first_is_kept()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIdempotencyStore, HostStore>();

        // Act
        using var provider = services.AddFrappeHttp().BuildServiceProvider();

        // Assert
        await Assert.That(provider.GetRequiredService<IIdempotencyStore>()).IsTypeOf<HostStore>();
    }
}
