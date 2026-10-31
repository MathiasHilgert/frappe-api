using Frappe.Geo.Infrastructure.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Geo.Infrastructure.Subdivisions;

internal sealed class SubdivisionConfiguration
    : IEntityTypeConfiguration<SubdivisionRow>,
        IEntityTypeConfiguration<SubdivisionNameRow>
{
    public void Configure(EntityTypeBuilder<SubdivisionRow> builder)
    {
        builder.ToTable(
            "subdivisions",
            static table =>
            {
                table.HasCheckConstraint(
                    "ck_subdivisions_subdivision_code",
                    "subdivision_code ~ '^[A-Z]{2}-[A-Z0-9]{1,3}$'"
                );
                table.HasCheckConstraint(
                    "ck_subdivisions_status",
                    "status IN ('Active', 'Retired')"
                );
            }
        );
        builder.HasKey(static subdivision => subdivision.SubdivisionCode);
        builder.Property(static subdivision => subdivision.Status).HasConversion<string>();
        builder
            .HasOne<CountryRow>()
            .WithMany()
            .HasForeignKey(static subdivision => subdivision.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SubdivisionNameRow> builder)
    {
        builder.ToTable(
            "subdivision_names",
            static table => table.HasCheckConstraint("ck_subdivision_names_name", "name <> ''")
        );
        builder.HasKey(static name => new { name.SubdivisionCode, name.Locale });
        builder
            .HasOne<SubdivisionRow>()
            .WithMany()
            .HasForeignKey(static name => name.SubdivisionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
