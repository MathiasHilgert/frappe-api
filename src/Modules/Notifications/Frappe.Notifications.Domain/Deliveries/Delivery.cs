using System.Diagnostics;
using Frappe.Aggregates;
using Frappe.Localization;
using Frappe.Results;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// One notification of one kind to one recipient over one channel: the record that dedupes redelivery (S14) and follows the
/// provider's feedback. It holds no content, no address and no secret (S13): the recipient is a key, an address only a keyed hash.
/// </summary>
public sealed class Delivery : AggregateRoot
{
    private Delivery()
    {
        Kind = null!;
        Channel = null!;
        RecipientKey = null!;
    }

    private Delivery(Guid id, DeliveryTarget target, DeliveryStatus status, Instant now)
    {
        Id = id;
        SourceMessageId = target.SourceMessageId;
        Kind = target.Kind;
        Category = target.Category;
        Channel = target.Channel.Value;
        RecipientKey = target.RecipientKey.Value;
        AddressHash = target.AddressHash?.Value;
        HashKeyId = target.AddressHash?.KeyId;
        Status = status;
        CreatedAt = now;
        StatusChangedAt = now;
    }

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The id of the message that asked for it; with <see cref="Kind"/>, <see cref="RecipientKey"/> and <see cref="Channel"/> it is unique.
    /// </summary>
    public Guid SourceMessageId { get; private set; }

    /// <summary>The notification kind, lower snake case.</summary>
    public string Kind { get; private set; }

    /// <summary>The kind's category when it was sent: a deliberate snapshot, so a later reclassification never rewrites history (DATA-12).</summary>
    public NotificationCategory Category { get; private set; }

    /// <summary>The channel's name; an open value, not a closed set.</summary>
    public string Channel { get; private set; }

    /// <summary><c>ref:&lt;kind&gt;:&lt;id&gt;</c> for a reference, <c>addr:&lt;key id&gt;:&lt;hex hash&gt;</c> for an address.</summary>
    public string RecipientKey { get; private set; }

    /// <summary>The keyed hash of the normalized address, when the recipient is an address.</summary>
    public byte[]? AddressHash { get; private set; }

    /// <summary>The id of the key that made <see cref="AddressHash"/>.</summary>
    public short? HashKeyId { get; private set; }

    /// <summary>The language the notification was rendered in.</summary>
    public LanguageTag? LocaleServed { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public DeliveryFailureReason? FailureReason { get; private set; }

    public DeliveryProvider? Provider { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public Instant CreatedAt { get; private set; }

    public Instant? SentAt { get; private set; }

    public Instant? DeliveredAt { get; private set; }

    public Instant StatusChangedAt { get; private set; }

    /// <summary>A delivery the provider took: it waits for the provider's feedback.</summary>
    public static Delivery Sent(
        Guid id, DeliveryTarget target, DeliveryProvider provider, string providerMessageId, LanguageTag? localeServed, Instant now)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        return new Delivery(id, target, DeliveryStatus.Sent, now)
        {
            Provider = provider,
            ProviderMessageId = providerMessageId,
            LocaleServed = localeServed,
            SentAt = now,
        };
    }

    /// <summary>A delivery that will never be sent, for a coded reason.</summary>
    public static Delivery Failed(Guid id, DeliveryTarget target, DeliveryFailureReason reason, Instant now)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new Delivery(id, target, DeliveryStatus.Failed, now) { FailureReason = reason };
    }

    /// <summary>A delivery whose recipient reference no directory resolved (S3): final, never retried.</summary>
    public static Delivery Unresolved(Guid id, DeliveryTarget target, Instant now)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new Delivery(id, target, DeliveryStatus.Unresolved, now);
    }

    /// <summary>
    /// Moves the delivery as the provider reports. Allowed: sent to delivered, bounced, complained or delayed; delayed to delivered or
    /// bounced; delivered to complained. Anything else is refused, so a terminal status never regresses; repeating the current status
    /// and a provider suppression (which says nothing about this message) change nothing.
    /// </summary>
    public Result<Unit> Apply(DeliveryFeedback feedback, Instant now)
    {
        var target = feedback switch
        {
            DeliveryFeedback.Delivered => DeliveryStatus.Delivered,
            DeliveryFeedback.PermanentFailure => DeliveryStatus.Bounced,
            DeliveryFeedback.Complaint => DeliveryStatus.Complained,
            DeliveryFeedback.Delayed => DeliveryStatus.Delayed,
            DeliveryFeedback.SuppressedByProvider => (DeliveryStatus?)null,
            _ => throw new UnreachableException($"Unknown feedback {feedback}."),
        };

        if (target is null || target == Status)
        {
            return Unit.Value;
        }

        if (!CanMoveTo(target.Value))
        {
            return Error.RuleViolation(
                "notifications.delivery.transition_not_allowed",
                ("from", Status.ToString()),
                ("feedback", feedback.ToString()));
        }

        Status = target.Value;
        StatusChangedAt = now;
        if (target == DeliveryStatus.Delivered)
        {
            DeliveredAt = now;
        }

        return Unit.Value;
    }

    private bool CanMoveTo(DeliveryStatus target) => Status switch
    {
        DeliveryStatus.Sent => target is DeliveryStatus.Delivered or DeliveryStatus.Bounced or DeliveryStatus.Complained or DeliveryStatus.Delayed,
        DeliveryStatus.Delayed => target is DeliveryStatus.Delivered or DeliveryStatus.Bounced,
        DeliveryStatus.Delivered => target is DeliveryStatus.Complained,
        DeliveryStatus.Bounced or DeliveryStatus.Complained or DeliveryStatus.Failed or DeliveryStatus.Unresolved => false,
        _ => throw new UnreachableException($"Unknown status {Status}."),
    };
}
