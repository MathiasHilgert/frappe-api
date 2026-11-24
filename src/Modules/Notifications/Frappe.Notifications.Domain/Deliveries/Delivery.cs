using Frappe.Aggregates;
using Frappe.Localization;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters; behavior arrives with the pipeline.
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
}
