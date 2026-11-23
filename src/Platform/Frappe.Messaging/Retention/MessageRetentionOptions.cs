using System.ComponentModel.DataAnnotations;
using Frappe.Messaging.JetStream;

namespace Frappe.Messaging.Retention;

/// <summary>
/// How long a module keeps its published outbox rows and its inbox records before the purge deletes them, bound from
/// <see cref="Section"/> (<c>Messaging:Retention:OutboxDays</c>, <c>InboxDays</c>, <c>BatchSize</c>) and validated at
/// startup by <see cref="MessageRetentionOptionsValidator"/>.
/// </summary>
public sealed class MessageRetentionOptions : IValidatableObject
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Messaging:Retention";

    /// <summary>The section the options were bound from, so a rejection names the module's real key.</summary>
    internal string ConfigurationSection { get; set; } = Section;

    /// <summary>The longest retention accepted, in days: ten years.</summary>
    public const int MaxDays = 3650;

    /// <summary>
    /// <c>Messaging:Retention:OutboxDays</c>: whole days after which a published outbox row is deleted; see <see
    /// cref="OutboxRetention"/>. Default 7. Range 1 to <see cref="MaxDays"/>. Raising it keeps published events
    /// longer for support and grows the table; unpublished rows are never deleted.
    /// </summary>
    public int OutboxDays { get; set; } = 7;

    /// <summary>
    /// <c>Messaging:Retention:InboxDays</c>: whole days after which an inbox record is deleted; see <see
    /// cref="InboxRetention"/>. Default 14. Range from the stream's MaxAge in days to <see cref="MaxDays"/>. Below
    /// the stream's MaxAge a redelivered message whose record is gone would run its handler twice, so the minimum is
    /// enforced at startup.
    /// </summary>
    public int InboxDays { get; set; } = 14;

    /// <summary>
    /// <c>Messaging:Retention:BatchSize</c>: the most rows one delete statement removes, so no statement holds its
    /// locks for long. Default 1000. Range 1 or more. Raising it purges faster with longer locks; lowering it does
    /// the opposite.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Age, measured from <c>published_at</c>, after which a published outbox row is deleted. Unpublished rows, parked
    /// ones included, are never deleted: they are the only copy of an event the broker has not stored.
    /// </summary>
    public TimeSpan OutboxRetention => TimeSpan.FromDays(OutboxDays);

    /// <summary>
    /// Age, measured from <c>received_at</c>, after which an inbox record is deleted. Consumers deliver from the start
    /// of the stream, so a record must outlive every message the stream still holds
    /// (<see cref="JetStreamTopology.ModuleRetention"/>), or a redelivery would run a handler twice.
    /// </summary>
    public TimeSpan InboxRetention => TimeSpan.FromDays(InboxDays);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var minimumInboxDays = (int)Math.Ceiling(JetStreamTopology.ModuleRetention.TotalDays);
        if (OutboxDays is < 1 or > MaxDays)
        {
            yield return Rejected(
                nameof(OutboxDays),
                $"must be a whole number of days from 1 (a day) to {MaxDays}, not '{OutboxDays}'.");
        }

        if (InboxDays < minimumInboxDays || InboxDays > MaxDays)
        {
            yield return Rejected(
                nameof(InboxDays),
                $"must be a whole number of days from {minimumInboxDays} (the stream MaxAge) to {MaxDays}, not '{InboxDays}': "
                + "a redelivered message whose inbox record is gone would be handled again.");
        }

        if (BatchSize < 1)
        {
            yield return Rejected(nameof(BatchSize), $"must be at least 1, not '{BatchSize}'.");
        }
    }

    private ValidationResult Rejected(string key, string reason) =>
        new($"Configuration '{ConfigurationSection}:{key}' {reason}", [key]);
}
