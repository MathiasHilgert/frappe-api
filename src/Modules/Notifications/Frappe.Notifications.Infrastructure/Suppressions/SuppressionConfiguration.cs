using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Notifications.Infrastructure.Suppressions;

/// <summary>Maps <see cref="Suppression"/> to <c>suppressions</c>, keyed by channel and the keyed hash of the address.</summary>
internal sealed class SuppressionConfiguration : IEntityTypeConfiguration<Suppression>
{
    public const string Table = "suppressions";

    public const string PrimaryKey = "pk_suppressions";

    /// <summary>An address hash is a SHA-256 value.</summary>
    public const string AddressHashLength = "ck_suppressions_address_hash_length";

    public const string AddressHashLengthSql = "octet_length(address_hash) = 32";

    /// <summary>The provider and its event id come together; nothing enforces that the event still exists (a reference, not a key).</summary>
    public const string ProviderPair = "ck_suppressions_provider_pair";

    public const string ProviderPairSql = "(provider IS NULL) = (provider_event_id IS NULL)";

    public const int ProviderEventIdLength = 100;

    public void Configure(EntityTypeBuilder<Suppression> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint(AddressHashLength, AddressHashLengthSql);
            table.HasCheckConstraint("ck_suppressions_reason", $"reason IN ({StoredEnum<SuppressionReason>.CheckList})");
            table.HasCheckConstraint("ck_suppressions_source", $"source IN ({StoredEnum<SuppressionSource>.CheckList})");
            table.HasCheckConstraint("ck_suppressions_provider", $"provider IN ({StoredEnum<DeliveryProvider>.CheckList})");
            table.HasCheckConstraint(ProviderPair, ProviderPairSql);
        });
        builder.HasKey(s => new { s.Channel, s.AddressHash }).HasName(PrimaryKey);
        builder.Property(s => s.Channel).HasColumnName("channel").HasMaxLength(30);
        builder.Property(s => s.AddressHash).HasColumnName("address_hash");
        builder.Property(s => s.HashKeyId).HasColumnName("hash_key_id");
        builder.Property(s => s.Reason).HasColumnName("reason")
            .HasConversion(StoredEnum<SuppressionReason>.Converter).HasMaxLength(StoredEnum<SuppressionReason>.MaxLength);
        builder.Property(s => s.Source).HasColumnName("source")
            .HasConversion(StoredEnum<SuppressionSource>.Converter).HasMaxLength(StoredEnum<SuppressionSource>.MaxLength);
        builder.Property(s => s.Provider).HasColumnName("provider")
            .HasConversion(StoredEnum<DeliveryProvider>.Converter).HasMaxLength(StoredEnum<DeliveryProvider>.MaxLength);
        builder.Property(s => s.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(ProviderEventIdLength);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}
