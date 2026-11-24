using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Notifications.Infrastructure.Preferences;

/// <summary>Maps <see cref="Preference"/> to <c>preferences</c>: a row exists only while a recipient has a category turned off.</summary>
internal sealed class PreferenceConfiguration : IEntityTypeConfiguration<Preference>
{
    public const string Table = "preferences";

    public const string PrimaryKey = "pk_preferences";

    /// <summary>Only marketing can be turned off (S9): Security and Transactional rows are refused.</summary>
    public const string OnlyMarketing = "ck_preferences_only_marketing";

    public static string OnlyMarketingSql { get; } = $"category = '{StoredEnum<NotificationCategory>.ToText(NotificationCategory.Marketing)}'";

    public void Configure(EntityTypeBuilder<Preference> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_preferences_recipient_key_format", "recipient_key ~ '^(ref:[a-z_]+:.+|addr:[0-9]+:[0-9a-f]{64})$'");
            table.HasCheckConstraint(OnlyMarketing, OnlyMarketingSql);
        });
        builder.HasKey(p => new { p.RecipientKey, p.Category }).HasName(PrimaryKey);
        builder.Property(p => p.RecipientKey).HasColumnName("recipient_key").HasMaxLength(150);
        builder.Property(p => p.Category).HasColumnName("category")
            .HasConversion(StoredEnum<NotificationCategory>.Converter).HasMaxLength(StoredEnum<NotificationCategory>.MaxLength);
        builder.Property(p => p.OptedOutAt).HasColumnName("opted_out_at");
    }
}
