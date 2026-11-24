using Frappe.Notifications.Domain.Deliveries;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Notifications.Infrastructure.Deliveries;

/// <summary>
/// Maps <see cref="Delivery"/> to <c>deliveries</c>: its closed sets, its invariants as check constraints and the two uniqueness rules
/// that dedupe it.
/// </summary>
internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public const string Table = "deliveries";

    /// <summary>A kind is lower snake case, starting with a letter.</summary>
    public const string KindFormat = "ck_deliveries_kind_format";

    public const string KindFormatSql = "kind ~ '^[a-z][a-z0-9_]*$'";

    /// <summary>A recipient key is a reference (<c>ref:kind:id</c>) or an address hash (<c>addr:key id:64 hex digits</c>).</summary>
    public const string RecipientKeyFormat = "ck_deliveries_recipient_key_format";

    public const string RecipientKeyFormatSql = "recipient_key ~ '^(ref:[a-z_]+:.+|addr:[0-9]+:[0-9a-f]{64})$'";

    /// <summary>An address hash is a SHA-256 value.</summary>
    public const string AddressHashLength = "ck_deliveries_address_hash_length";

    public const string AddressHashLengthSql = "address_hash IS NULL OR octet_length(address_hash) = 32";

    /// <summary>A hash and the key that made it come together.</summary>
    public const string AddressHashPair = "ck_deliveries_address_hash_pair";

    public const string AddressHashPairSql = "(address_hash IS NULL) = (hash_key_id IS NULL)";

    /// <summary>A provider and its message id come together.</summary>
    public const string ProviderPair = "ck_deliveries_provider_pair";

    public const string ProviderPairSql = "(provider IS NULL) = (provider_message_id IS NULL)";

    /// <summary>A status the provider reports on needs the provider's message id and the instant it was sent.</summary>
    public const string SentLike = "ck_deliveries_sent_like";

    /// <summary>A delivery is failed exactly when it has a failure reason.</summary>
    public const string FailedHasReason = "ck_deliveries_failed_reason";

    public const string FailedHasReasonSql = "(status = 'failed') = (failure_reason IS NOT NULL)";

    /// <summary>
    /// The same message, kind, recipient and channel never record twice (S14); nulls count as equal, so a missing part cannot slip through.
    /// </summary>
    public const string IdempotencyIndex = "ux_deliveries_message_kind_recipient_channel";

    /// <summary>A provider's message id belongs to one delivery. Partial: only a delivery the provider took has one.</summary>
    public const string ProviderMessageIndex = "ux_deliveries_provider_message";

    /// <summary>
    /// The predicate of <see cref="ProviderMessageIndex"/>; the webhook's lookup repeats it word for word so Postgres can use the index.
    /// </summary>
    public const string ProviderMessageFilter = "provider_message_id IS NOT NULL";

    /// <summary>The retention purge reads deliveries older than a cutoff (S11).</summary>
    public const string CreatedAtIndex = "ix_deliveries_created_at";

    public const string PrimaryKey = "pk_deliveries";

    /// <summary>The longest provider message id stored.</summary>
    public const int ProviderMessageIdLength = 100;

    /// <summary>The statuses a provider reports on: they only exist after the provider took the message.</summary>
    private static readonly DeliveryStatus[] SentLikeStatuses =
        [DeliveryStatus.Sent, DeliveryStatus.Delivered, DeliveryStatus.Delayed, DeliveryStatus.Bounced, DeliveryStatus.Complained];

    public static string SentLikeSql { get; } =
        $"status NOT IN ({string.Join(", ", SentLikeStatuses.Select(status => $"'{StoredEnum<DeliveryStatus>.ToText(status)}'"))}) "
        + "OR (provider_message_id IS NOT NULL AND sent_at IS NOT NULL)";

    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(KindFormat, KindFormatSql);
            table.HasCheckConstraint("ck_deliveries_category", $"category IN ({StoredEnum<NotificationCategory>.CheckList})");
            table.HasCheckConstraint(RecipientKeyFormat, RecipientKeyFormatSql);
            table.HasCheckConstraint(AddressHashLength, AddressHashLengthSql);
            table.HasCheckConstraint(AddressHashPair, AddressHashPairSql);
            table.HasCheckConstraint("ck_deliveries_status", $"status IN ({StoredEnum<DeliveryStatus>.CheckList})");
            table.HasCheckConstraint("ck_deliveries_failure_reason", $"failure_reason IN ({StoredEnum<DeliveryFailureReason>.CheckList})");
            table.HasCheckConstraint("ck_deliveries_provider", $"provider IN ({StoredEnum<DeliveryProvider>.CheckList})");
            table.HasCheckConstraint(ProviderPair, ProviderPairSql);
            table.HasCheckConstraint(SentLike, SentLikeSql);
            table.HasCheckConstraint(FailedHasReason, FailedHasReasonSql);
        });
        builder.HasKey(d => d.Id).HasName(PrimaryKey);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(d => d.SourceMessageId).HasColumnName("source_message_id");
        builder.Property(d => d.Kind).HasColumnName("kind").HasMaxLength(100);

        // A snapshot of the kind's category at send time, kept on purpose (DATA-12): a later reclassification must not rewrite history.
        builder.Property(d => d.Category).HasColumnName("category")
            .HasConversion(StoredEnum<NotificationCategory>.Converter).HasMaxLength(StoredEnum<NotificationCategory>.MaxLength);
        builder.Property(d => d.Channel).HasColumnName("channel").HasMaxLength(30);
        builder.Property(d => d.RecipientKey).HasColumnName("recipient_key").HasMaxLength(150);
        builder.Property(d => d.AddressHash).HasColumnName("address_hash");
        builder.Property(d => d.HashKeyId).HasColumnName("hash_key_id");
        builder.Property(d => d.LocaleServed).HasColumnName("locale_served");
        builder.Property(d => d.Status).HasColumnName("status")
            .HasConversion(StoredEnum<DeliveryStatus>.Converter).HasMaxLength(StoredEnum<DeliveryStatus>.MaxLength);
        builder.Property(d => d.FailureReason).HasColumnName("failure_reason")
            .HasConversion(StoredEnum<DeliveryFailureReason>.Converter).HasMaxLength(StoredEnum<DeliveryFailureReason>.MaxLength);
        builder.Property(d => d.Provider).HasColumnName("provider")
            .HasConversion(StoredEnum<DeliveryProvider>.Converter).HasMaxLength(StoredEnum<DeliveryProvider>.MaxLength);
        builder.Property(d => d.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(ProviderMessageIdLength);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.SentAt).HasColumnName("sent_at");
        builder.Property(d => d.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(d => d.StatusChangedAt).HasColumnName("status_changed_at");

        builder.HasIndex(d => new { d.SourceMessageId, d.Kind, d.RecipientKey, d.Channel }, IdempotencyIndex)
            .HasDatabaseName(IdempotencyIndex)
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(d => new { d.Provider, d.ProviderMessageId }, ProviderMessageIndex)
            .HasDatabaseName(ProviderMessageIndex)
            .IsUnique()
            .HasFilter(ProviderMessageFilter);
        builder.HasIndex(d => d.CreatedAt).HasDatabaseName(CreatedAtIndex);
    }
}
