using Frappe.Persistence;
using Microsoft.Extensions.Time.Testing;
using NodaTime;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>NodaTime's clock reads the process's <see cref="TimeProvider"/>; design time reads a fixed one.</summary>
public sealed class ClockTests
{
    [Test]
    public async Task The_clock_reads_the_instant_of_the_time_provider_and_follows_it()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var clock = new TimeProviderClock(time);

        // Act
        var first = clock.GetCurrentInstant();
        time.Advance(TimeSpan.FromMinutes(1));
        var second = clock.GetCurrentInstant();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(Instant.FromUtc(2030, 1, 2, 3, 4, 5));
            await Assert.That(second).IsEqualTo(Instant.FromUtc(2030, 1, 2, 3, 5, 5));
        }
    }

    [Test]
    public async Task The_design_time_clock_always_reads_the_unix_epoch()
    {
        // Act
        var instant = DesignTimeClock.Fixed.GetCurrentInstant();

        // Assert
        await Assert.That(instant).IsEqualTo(Instant.FromUnixTimeSeconds(0));
    }
}
