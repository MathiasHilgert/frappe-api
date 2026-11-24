using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Deliveries;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Notifications.Infrastructure.ProviderEvents;

/// <summary>Maps <see cref="ProviderEventRow"/> to <c>provider_events</c>, purged after seven days by <c>received_at</c>.</summary>
internal sealed class ProviderEventRowConfiguration : IEntityTypeConfiguration<ProviderEventRow>
{
    public const string Table = "provider_events";

    public const string PrimaryKey = "pk_provider_events";

    /// <summary>What a parked event holds comes together: the message it is about, what was said and when.</summary>
    public const string ParkedTogether = "ck_provider_events_parked_together";

    public const string ParkedTogetherSql =
        "(provider_message_id IS NULL) = (feedback IS NULL) AND (feedback IS NULL) = (occurred_at IS NULL)";

    /// <summary>The retention purge reads events older than a cutoff.</summary>
    public const string ReceivedAtIndex = "ix_provider_events_received_at";

    public const int EventIdLength = 100;

    public void Configure(EntityTypeBuilder<ProviderEventRow> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_provider_events_provider", $"provider IN ({StoredEnum<DeliveryProvider>.CheckList})");
            table.HasCheckConstraint("ck_provider_events_feedback", $"feedback IN ({StoredEnum<DeliveryFeedback>.CheckList})");
            table.HasCheckConstraint(ParkedTogether, ParkedTogetherSql);
        });
        builder.HasKey(e => new { e.Provider, e.EventId }).HasName(PrimaryKey);
        builder.Property(e => e.Provider).HasColumnName("provider")
            .HasConversion(StoredEnum<DeliveryProvider>.Converter).HasMaxLength(StoredEnum<DeliveryProvider>.MaxLength);
        builder.Property(e => e.EventId).HasColumnName("event_id").HasMaxLength(EventIdLength);
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at");
        builder.Property(e => e.Matched).HasColumnName("matched");
        builder.Property(e => e.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(DeliveryConfiguration.ProviderMessageIdLength);
        builder.Property(e => e.Feedback).HasColumnName("feedback")
            .HasConversion(StoredEnum<DeliveryFeedback>.Converter).HasMaxLength(StoredEnum<DeliveryFeedback>.MaxLength);
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        builder.HasIndex(e => e.ReceivedAt).HasDatabaseName(ReceivedAtIndex);
    }
}
