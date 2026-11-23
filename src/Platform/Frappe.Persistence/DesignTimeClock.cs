using NodaTime;

namespace Frappe.Persistence;

/// <summary>
/// The clock the <c>dotnet ef</c> design-time factories build their contexts with: it never connects to a database and
/// reads no wall clock, so the model it builds is the same on every run (CRAFT-12, CS-10).
/// </summary>
public static class DesignTimeClock
{
    /// <summary>A clock that always reads the Unix epoch.</summary>
    public static IClock Fixed { get; } = new TimeProviderClock(new EpochTimeProvider());

    /// <summary>A time provider stuck at the Unix epoch.</summary>
    private sealed class EpochTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
