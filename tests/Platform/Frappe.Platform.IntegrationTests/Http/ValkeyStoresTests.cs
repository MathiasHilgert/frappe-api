using Frappe.Caching;
using Frappe.Http;
using Frappe.Http.Idempotency;
using Frappe.Http.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>The rate limit windows and the idempotent answers every replica shares, against a real Valkey.</summary>
public sealed class ValkeyStoresTests
{
    [ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
    public required ValkeyServer Valkey { get; init; }

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private ServiceProvider Services() => new ServiceCollection()
        .AddLogging()
        .AddFrappeCaching(Valkey.ConnectionString, $"it-{Guid.NewGuid():N}")
        .BuildServiceProvider();

    [Test]
    public async Task A_window_counts_every_hit_of_a_client_and_keeps_clients_apart()
    {
        // Arrange
        await using var services = Services();
        var store = new ValkeyRateLimitStore(services.GetRequiredService<ValkeyWindowCounter>());

        // Act
        var first = await store.HitAsync("anonymous:203.0.113.1", 1, Window, CancellationToken.None);
        var second = await store.HitAsync("anonymous:203.0.113.1", 1, Window, CancellationToken.None);
        var other = await store.HitAsync("anonymous:203.0.113.2", 1, Window, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Count).IsEqualTo(1);
            await Assert.That(second.Count).IsEqualTo(2);
            await Assert.That(other.Count).IsEqualTo(1);
            await Assert.That(second.Remaining).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Window);
        }
    }

    [Test]
    public async Task A_key_is_claimed_once_then_in_flight_then_replayed_with_the_stored_answer()
    {
        // Arrange
        await using var services = Services();
        var store = new ValkeyIdempotencyStore(services.GetRequiredService<ValkeyValues>());
        var key = Guid.NewGuid().ToString("N");
        var answer = new StoredResponse(201, new Dictionary<string, string[]> { ["Location"] = ["/v1/orders/1"] }, [1, 2, 3]);

        var claimed = await store.ClaimAsync(key, "fingerprint", CancellationToken.None);
        var inFlight = await store.ClaimAsync(key, "fingerprint", CancellationToken.None);
        var reused = await store.ClaimAsync(key, "another", CancellationToken.None);
        await store.CompleteAsync(key, "fingerprint", answer, CancellationToken.None);

        // Act
        var replay = await store.ClaimAsync(key, "fingerprint", CancellationToken.None);
        var reusedAfter = await store.ClaimAsync(key, "another", CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(claimed.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
            await Assert.That(inFlight.Outcome).IsEqualTo(IdempotencyOutcome.InFlight);
            await Assert.That(reused.Outcome).IsEqualTo(IdempotencyOutcome.Reused);
            await Assert.That(replay.Outcome).IsEqualTo(IdempotencyOutcome.Replay);
            await Assert.That(replay.Response!.Status).IsEqualTo(201);
            await Assert.That(replay.Response.Headers["Location"]).IsEquivalentTo(["/v1/orders/1"]);
            await Assert.That(replay.Response.Body).IsEquivalentTo(new byte[] { 1, 2, 3 });
            await Assert.That(reusedAfter.Outcome).IsEqualTo(IdempotencyOutcome.Reused);
        }
    }

    [Test]
    public async Task A_released_key_can_be_claimed_again_by_a_retry()
    {
        // Arrange
        await using var services = Services();
        var store = new ValkeyIdempotencyStore(services.GetRequiredService<ValkeyValues>());
        var key = Guid.NewGuid().ToString("N");
        await store.ClaimAsync(key, "fingerprint", CancellationToken.None);
        await store.ReleaseAsync(key, CancellationToken.None);

        // Act
        var retry = await store.ClaimAsync(key, "fingerprint", CancellationToken.None);

        // Assert
        await Assert.That(retry.Outcome).IsEqualTo(IdempotencyOutcome.Claimed);
    }
}
