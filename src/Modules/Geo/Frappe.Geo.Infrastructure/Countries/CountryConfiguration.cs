using Frappe.Geo.Infrastructure.Currencies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Countries;

internal sealed class CountryConfiguration
    : IEntityTypeConfiguration<CountryRow>,
        IEntityTypeConfiguration<CountryNameRow>
{
    public void Configure(EntityTypeBuilder<CountryRow> builder)
    {
        builder.ToTable(
            "countries",
            static table =>
            {
                table.HasCheckConstraint(
                    "ck_countries_country_code",
                    "country_code ~ '^[A-Z]{2}$'"
                );
                table.HasCheckConstraint("ck_countries_alpha3", "alpha3 ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint(
                    "ck_countries_numeric_code",
                    "numeric_code ~ '^[0-9]{3}$'"
                );
                table.HasCheckConstraint(
                    "ck_countries_calling_code",
                    "calling_code ~ '^[0-9]{1,3}$'"
                );
                table.HasCheckConstraint("ck_countries_status", "status IN ('Active', 'Retired')");
            }
        );
        builder.HasKey(static country => country.CountryCode);
        builder.HasIndex(static country => country.Alpha3).IsUnique();
        builder.HasIndex(static country => country.NumericCode).IsUnique();
        builder.Property(static country => country.Status).HasConversion<string>();
        builder
            .HasOne<CurrencyRow>()
            .WithMany()
            .HasForeignKey(static country => country.DefaultCurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<CountryNameRow> builder)
    {
        builder.ToTable(
            "country_names",
            static table => table.HasCheckConstraint("ck_country_names_name", "name <> ''")
        );
        builder.HasKey(static name => new { name.CountryCode, name.Locale });
        builder
            .HasOne<CountryRow>()
            .WithMany()
            .HasForeignKey(static name => name.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
