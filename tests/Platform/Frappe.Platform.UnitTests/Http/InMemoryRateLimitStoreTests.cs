using Frappe.Http;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The windows a process counts on its own when no shared store is registered.</summary>
public sealed class InMemoryRateLimitStoreTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    [Test]
    public async Task A_window_opens_on_its_first_hit_and_later_hits_never_extend_it()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore(clock);
        var first = await store.HitAsync("anonymous:client", 1, Window, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(20));

        // Act
        var second = await store.HitAsync("anonymous:client", 2, Window, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(new RateLimitWindow(1, Window));
            await Assert.That(second).IsEqualTo(new RateLimitWindow(3, TimeSpan.FromSeconds(40)));
        }
    }

    [Test]
    public async Task A_closed_window_starts_over_and_each_key_counts_apart()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore(clock);
        await store.HitAsync("anonymous:one", 5, Window, CancellationToken.None);
        clock.Advance(Window);

        // Act
        var again = await store.HitAsync("anonymous:one", 1, Window, CancellationToken.None);
        var other = await store.HitAsync("anonymous:two", 1, Window, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(again).IsEqualTo(new RateLimitWindow(1, Window));
            await Assert.That(other).IsEqualTo(new RateLimitWindow(1, Window));
        }
    }

    [Test]
    public async Task Windows_that_closed_are_dropped_as_new_hits_arrive()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore(clock);
        await store.HitAsync("anonymous:gone", 1, TimeSpan.FromSeconds(10), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        await store.HitAsync("anonymous:other", 1, Window, CancellationToken.None);
        var returned = await store.HitAsync("anonymous:gone", 1, Window, CancellationToken.None);

        // Assert
        await Assert.That(returned).IsEqualTo(new RateLimitWindow(1, Window));
    }
}
