using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Subdivisions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Cities;

internal sealed class CityConfiguration : IEntityTypeConfiguration<CityRow>
{
    // Byte order, so the btree serves prefix LIKE and keyset order needs no locale.
    private const string ByteOrder = "C";

    public void Configure(EntityTypeBuilder<CityRow> builder)
    {
        builder.ToTable(
            "cities",
            static table =>
            {
                table.HasCheckConstraint("ck_cities_indec_id", "indec_id ~ '^[0-9]+$'");
                table.HasCheckConstraint("ck_cities_name", "name <> ''");
                table.HasCheckConstraint("ck_cities_time_zone_id", "time_zone_id <> ''");
                table.HasCheckConstraint("ck_cities_status", "status IN ('Active', 'Retired')");
            }
        );
        builder.HasKey(static city => city.CityId);
        builder.Property(static city => city.CityId).ValueGeneratedNever();
        builder.Property(static city => city.SearchName).UseCollation(ByteOrder);
        builder.Property(static city => city.SearchText).UseCollation(ByteOrder);
        builder.Property(static city => city.TimeZoneId).HasConversion<TimeZoneIdConverter>();
        builder.Property(static city => city.Status).HasConversion<string>();
        builder.HasIndex(static city => city.IndecId).IsUnique();
        builder
            .HasIndex(static city => new
            {
                city.CountryCode,
                city.SearchName,
                city.IndecId,
            })
            .HasDatabaseName("ix_cities_country_search");
        builder
            .HasIndex(static city => new
            {
                city.SubdivisionCode,
                city.SearchName,
                city.IndecId,
            })
            .HasDatabaseName("ix_cities_subdivision_search");
        builder
            .HasIndex(static city => new { city.SearchName, city.IndecId })
            .HasDatabaseName("ix_cities_search");
        builder
            .HasIndex(static city => city.SearchText)
            .HasDatabaseName("ix_cities_search_text_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");
        builder
            .HasOne<CountryRow>()
            .WithMany()
            .HasForeignKey(static city => city.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<SubdivisionRow>()
            .WithMany()
            .HasForeignKey(static city => city.SubdivisionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
