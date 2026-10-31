using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Locales;

internal sealed class LocaleConfiguration
    : IEntityTypeConfiguration<LocaleRow>,
        IEntityTypeConfiguration<LocaleNameRow>
{
    public void Configure(EntityTypeBuilder<LocaleRow> builder)
    {
        builder.ToTable(
            "locales",
            static table =>
            {
                table.HasCheckConstraint(
                    "ck_locales_text_direction",
                    "text_direction IN ('ltr', 'rtl')"
                );
                table.HasCheckConstraint("ck_locales_status", "status IN ('Active', 'Retired')");
            }
        );
        builder.HasKey(static locale => locale.LocaleTag);
        builder.Property(static locale => locale.Status).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<LocaleNameRow> builder)
    {
        builder.ToTable(
            "locale_names",
            static table => table.HasCheckConstraint("ck_locale_names_name", "name <> ''")
        );
        builder.HasKey(static name => new { name.LocaleTag, name.Locale });
        builder
            .HasOne<LocaleRow>()
            .WithMany()
            .HasForeignKey(static name => name.LocaleTag)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
