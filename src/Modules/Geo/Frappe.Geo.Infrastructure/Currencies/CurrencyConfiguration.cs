using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Currencies;

internal sealed class CurrencyConfiguration
    : IEntityTypeConfiguration<CurrencyRow>,
        IEntityTypeConfiguration<CurrencyNameRow>
{
    public void Configure(EntityTypeBuilder<CurrencyRow> builder)
    {
        builder.ToTable(
            "currencies",
            static table =>
            {
                table.HasCheckConstraint(
                    "ck_currencies_currency_code",
                    "currency_code ~ '^[A-Z]{3}$'"
                );
                table.HasCheckConstraint(
                    "ck_currencies_numeric_code",
                    "numeric_code ~ '^[0-9]{3}$'"
                );
                table.HasCheckConstraint("ck_currencies_minor_unit", "minor_unit BETWEEN 0 AND 4");
                table.HasCheckConstraint("ck_currencies_status", "status IN ('Active', 'Retired')");
            }
        );
        builder.HasKey(static currency => currency.CurrencyCode);
        builder.Property(static currency => currency.Status).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<CurrencyNameRow> builder)
    {
        builder.ToTable(
            "currency_names",
            static table => table.HasCheckConstraint("ck_currency_names_name", "name <> ''")
        );
        builder.HasKey(static name => new { name.CurrencyCode, name.Locale });
        builder
            .HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(static name => name.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
