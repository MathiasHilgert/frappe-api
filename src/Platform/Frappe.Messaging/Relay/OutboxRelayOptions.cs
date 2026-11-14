namespace Frappe.Messaging.Relay;

/// <summary>
/// The relay's tunables, bound from <see cref="Section"/> (for example <c>Messaging:Relay:BatchSize</c>) with today's
/// values as defaults, and validated at startup by <see cref="OutboxRelayOptionsValidator"/>.
/// </summary>
public sealed class OutboxRelayOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Messaging:Relay";

    /// <summary>Most messages taken by one pass.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Upper bound for one publish, so a hung broker never holds the database transaction open.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Pause of the hosted relay after a pass that did not fill a whole batch (nothing left to publish now).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound for a whole pass, so the batch transaction stays short even with a slow broker. Each publish gets
    /// whatever is left of it (capped by <see cref="PublishTimeout"/>), so a pass never runs longer than this.
    /// </summary>
    public TimeSpan MaxPassDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a relay owns the rows it claimed. It must outlive a whole pass plus one publish and the settle
    /// (<see cref="MaxPassDuration"/> + <see cref="PublishTimeout"/> + 5 s), so a live relay never loses its rows;
    /// a dead relay's rows come back after it.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Attempts before a message is parked: kept in the outbox, never retried automatically, logged as an error.
    /// Lowering it parks rows that already reached the new limit; raising it makes parked rows due again.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// Wait after the n-th failed attempt before the next one; the last value repeats. A configured list
    /// (<c>Messaging:Relay:RetryBackoff:0</c>, <c>:1</c>, ...) replaces this one whole.
    /// </summary>
    public IReadOnlyList<TimeSpan> RetryBackoff { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];
}
