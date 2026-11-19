using NodaTime;

namespace Frappe.Persistence;

/// <summary>
/// NodaTime's <see cref="IClock"/> over .NET's <see cref="TimeProvider"/>, so the whole process has one source of time:
/// persisted instants and timers (delays, deadlines, heartbeats) move together, and tests control both with one fake.
/// </summary>
public sealed class TimeProviderClock(TimeProvider time) : IClock
{
    public Instant GetCurrentInstant() => Instant.FromDateTimeOffset(time.GetUtcNow());
}
