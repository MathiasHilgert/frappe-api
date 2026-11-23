using System.ComponentModel.DataAnnotations;

namespace Frappe.Messaging.Relay;

/// <summary>
/// The relay's tunables, bound from <see cref="Section"/> (for example <c>Messaging:Relay:BatchSize</c>) with today's
/// values as defaults, and validated at startup by <see cref="OutboxRelayOptionsValidator"/>.
/// </summary>
public sealed class OutboxRelayOptions : IValidatableObject
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Messaging:Relay";

    /// <summary>The section the options were bound from, so a rejection names the module's real key.</summary>
    internal string ConfigurationSection { get; set; } = Section;

    /// <summary>
    /// <c>Messaging:Relay:BatchSize</c>: the most messages one pass takes. Default 50. Range 1 or more. Raising it
    /// drains a backlog faster and keeps the batch transaction open longer; lowering it does the opposite.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// <c>Messaging:Relay:PublishTimeout</c>: the upper bound for one publish, so a hung broker never holds the
    /// database transaction open. Default 5 s. Range greater than zero. Raising it tolerates a slower broker and
    /// lengthens the transaction; it also raises the minimum <see cref="LeaseDuration"/>.
    /// </summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <c>Messaging:Relay:PollInterval</c>: the pause of the hosted relay after a pass that did not fill a whole
    /// batch (nothing left to publish now). Default 1 s. Range greater than zero. Raising it saves database polls
    /// and adds that much latency to every event published while the relay idles.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// <c>Messaging:Relay:MaxPassDuration</c>: the upper bound for a whole pass, so the batch transaction stays
    /// short even with a slow broker. Default 10 s. Range greater than zero. Each publish gets whatever is left of
    /// it (capped by <see cref="PublishTimeout"/>), so a pass never runs longer than this; raising it raises the
    /// minimum <see cref="LeaseDuration"/>.
    /// </summary>
    public TimeSpan MaxPassDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <c>Messaging:Relay:LeaseDuration</c>: how long a relay owns the rows it claimed. Default 30 s. Range at
    /// least <see cref="MaxPassDuration"/> + <see cref="PublishTimeout"/> + 5 s, so a live relay never loses its
    /// rows. A dead relay's rows come back after it, so a longer lease delays that recovery.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <c>Messaging:Relay:SampleInterval</c>: how often an idle relay looks up its outbox for <see
    /// cref="MessagingMetrics.OutboxOldestAgeMetric"/> and <see cref="MessagingMetrics.OutboxParkedMetric"/>.
    /// Default 15 s. Range greater than zero. A pass that claimed rows looks them up at once, whatever this says;
    /// between lookups the age gauge keeps aging the last oldest message seen, so it never under-reports a backlog.
    /// Raising it lowers the database load and the gauge's freshness.
    /// </summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// <c>Messaging:Relay:MaxAttempts</c>: attempts before a message is parked: kept in the outbox, never retried
    /// automatically, logged as an error, until an operator requeues or deletes it. Default 10. Range 1 or more.
    /// Parked is stored in the row (no next attempt time), not derived from this value: raising it does not revive
    /// parked rows, and a row that already reached a lowered limit gets one more attempt and is parked if that
    /// fails too.
    /// </summary>
    /// <remarks>
    /// An operator lists a module's parked messages, as its data role or the owner (Orders' shown), fixes what made the
    /// broker reject them (most often a payload above the stream's size limit), then requeues each one, which gives it a
    /// fresh set of attempts for the next relay pass (consumers deduplicate by id, so a repeat is harmless), or deletes
    /// it when its event does not matter:
    /// <code>
    /// SELECT id, type, occurred_at, attempts, last_error FROM orders.outbox_messages
    /// WHERE published_at IS NULL AND attempts > 0 AND next_attempt_at IS NULL ORDER BY sequence;
    /// UPDATE orders.outbox_messages SET attempts = 0, next_attempt_at = NULL, last_error = NULL WHERE id = '{id}';
    /// DELETE FROM orders.outbox_messages WHERE id = '{id}';
    /// </code>
    /// </remarks>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// <c>Messaging:Relay:RetryBackoff</c>: the wait after the n-th failed attempt before the next one; the last
    /// value repeats. Default 1 s, 5 s, 30 s, 2 min, 10 min. Range a non-empty list of time spans. A configured
    /// list (<c>Messaging:Relay:RetryBackoff:0</c>, <c>:1</c>, ...) replaces the default whole. Longer waits spare
    /// a struggling broker and delay delivery of a retried event.
    /// </summary>
    public IReadOnlyList<TimeSpan> RetryBackoff { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BatchSize < 1)
        {
            yield return Rejected(nameof(BatchSize), $"must be at least 1, not '{BatchSize}'.");
        }

        if (MaxAttempts < 1)
        {
            yield return Rejected(nameof(MaxAttempts), $"must be at least 1, not '{MaxAttempts}'.");
        }

        foreach (var (key, value) in new[]
        {
            (nameof(PublishTimeout), PublishTimeout),
            (nameof(PollInterval), PollInterval),
            (nameof(MaxPassDuration), MaxPassDuration),
            (nameof(SampleInterval), SampleInterval),
        })
        {
            if (value <= TimeSpan.Zero)
            {
                yield return Rejected(key, $"must be positive, not '{value}'.");
            }
        }

        if (LeaseDuration < MaxPassDuration + PublishTimeout + TimeSpan.FromSeconds(5))
        {
            yield return Rejected(
                nameof(LeaseDuration),
                $"must be at least MaxPassDuration + PublishTimeout + 5 s, not '{LeaseDuration}'.");
        }

        if (RetryBackoff.Count == 0)
        {
            yield return Rejected(nameof(RetryBackoff), "must not be empty.");
        }
    }

    private ValidationResult Rejected(string key, string reason) =>
        new($"Configuration '{ConfigurationSection}:{key}' {reason}", [key]);
}
